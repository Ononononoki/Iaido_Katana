using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Iaido_Katana
{
    // the player's own parry sounds (Impact.OwnParrySounds): .wav files (16 bit) in BepInEx/config/Iaido_Katana/ParrySounds,
    // read once, one picked at random for each counter's impact in place of the mod's own impact sound. only heard by this
    // player, the mod ships none
    internal static class OwnParrySounds
    {
        internal const string FolderName = "Iaido_Katana/ParrySounds";
        private const string MixerPrefab = "sfx_metal_blocked"; // a game sound whose mixer group (sound effects) the files play through

        private static List<AudioClip> _clips;
        private static int _last = -1;

        internal static string Folder => Path.Combine(Path.Combine(BepInEx.Paths.ConfigPath, "Iaido_Katana"), "ParrySounds");

        // plays one of the files, true when one was played
        internal static bool Play()
        {
            if (!Plugin.OwnParrySoundsOn.Value || Plugin.OwnParrySoundVolume.Value <= 0f) return false;
            var clips = Clips();
            if (clips.Count == 0) return false;
            int pick = Random.Range(0, clips.Count);
            if (clips.Count > 1 && pick == _last) pick = (pick + 1 + Random.Range(0, clips.Count - 1)) % clips.Count; // never the same twice in a row
            _last = pick;
            var prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(MixerPrefab) : null;
            var template = prefab ? prefab.GetComponentInChildren<AudioSource>(true) : null;
            var holder = new GameObject("IaidoKatana_own_parry_sound");
            var source = holder.AddComponent<AudioSource>();
            source.clip = clips[pick];
            if (template) source.outputAudioMixerGroup = template.outputAudioMixerGroup;
            source.spatialBlend = 0f; // heard in full by the player who parried, like the mod's own impact sound
            source.volume = Mathf.Min(1f, Plugin.OwnParrySoundVolume.Value);
            source.Play();
            Object.Destroy(holder, clips[pick].length + 0.1f);
            if (Plugin.LogCounters.Value) Plugin.Log.LogInfo($"own parry sound: {clips[pick].name} ({pick + 1} of {clips.Count})");
            return true;
        }

        // the files, read once
        private static List<AudioClip> Clips()
        {
            if (_clips != null) return _clips;
            _clips = new List<AudioClip>();
            if (!Directory.Exists(Folder) || HeavySound.SetData == null) return _clips;
            var files = Directory.GetFiles(Folder, "*.wav");
            System.Array.Sort(files, System.StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                var samples = ReadWav(File.ReadAllBytes(file), out int channels, out int rate);
                if (samples == null)
                {
                    Plugin.Log.LogWarning($"own parry sound {Path.GetFileName(file)} is not a 16 bit wav, skipped");
                    continue;
                }
                var clip = AudioClip.Create(Path.GetFileNameWithoutExtension(file), samples.Length / channels, channels, rate, false);
                HeavySound.SetData.Invoke(clip, new object[] { samples, 0 });
                _clips.Add(clip);
            }
            Plugin.Log.LogInfo($"own parry sounds: {_clips.Count} loaded from {FolderName}");
            return _clips;
        }

        // a 16 bit pcm wav into samples, null for anything else
        internal static float[] ReadWav(byte[] wav, out int channels, out int rate)
        {
            channels = rate = 0;
            if (wav == null || wav.Length < 12 || System.Text.Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || System.Text.Encoding.ASCII.GetString(wav, 8, 4) != "WAVE") return null;
            int bits = 0, format = 0, data = -1, size = 0;
            for (int at = 12; at + 8 <= wav.Length; at += 8 + size + (size & 1))
            {
                string id = System.Text.Encoding.ASCII.GetString(wav, at, 4);
                size = System.BitConverter.ToInt32(wav, at + 4);
                if (id == "fmt " && at + 24 <= wav.Length)
                {
                    format = System.BitConverter.ToInt16(wav, at + 8);
                    channels = System.BitConverter.ToInt16(wav, at + 10);
                    rate = System.BitConverter.ToInt32(wav, at + 12);
                    bits = System.BitConverter.ToInt16(wav, at + 22);
                }
                else if (id == "data")
                {
                    data = at + 8;
                    break;
                }
            }
            if (data < 0 || format != 1 || bits != 16 || channels < 1 || rate <= 0) return null;
            int count = Mathf.Min(size, wav.Length - data) / 2;
            count -= count % channels;
            var samples = new float[count];
            for (int i = 0; i < count; i++) samples[i] = System.BitConverter.ToInt16(wav, data + 2 * i) / 32768f;
            return samples;
        }
    }
}
