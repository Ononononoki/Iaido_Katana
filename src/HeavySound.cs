using System.IO;
using UnityEngine;
using UnityEngine.Audio;

namespace Iaido_Katana
{
    // the steel katana's heavy, heard on every client with the mod (the user's picks of 15 oct): a swell from the button
    // press to the swing (the game's metal ring 7 at 0.8 with a dense echo, reversed, heavy_sound_listen6.py L1, shipped
    // in the dll because the game keeps that clip compressed), then the game's sword swing slowed and deepened, with a
    // light echo, loudest on the hit
    internal static class HeavySound
    {
        private const string SwingPrefab = "sfx_sword_swing", SwordClip = "Player_Movement_SwordSwing_M_03", OverClip = "Player_Movement_SwordSwing_Overlay_M_03";
        private const string SwellResource = "Iaido_Katana.heavy_swell.wav";
        // the levels against the swing's louder layer, and where each layer's sound starts (heavy_mix_numbers.py,
        // make_heavy_swell.py)
        private const float SwellVolume = 0.922f, SwordVolume = 0.93f, OverVolume = 1f;
        private const float SwordPitch = 0.7f, OverPitch = 0.75f, SwordHead = 0.034f, OverHead = 0.022f;
        internal const float SwingPeak = 0.186f; // seconds from the swing's start to its loudest moment
        private const float SwellPeak = 1.144f;  // seconds into the swell file where its rise ends (the swing starts there in the picked mix), its echo tail follows
        private const float Falloff = 40f;
        // the swing's light echo: the dry swing, then quieter copies
        private static readonly (float At, float Gain)[] SwingTaps = { (0f, 1f), (0.08f, 0.3f), (0.16f, 0.15f), (0.24f, 0.09f), (0.32f, 0.05f), (0.4f, 0.03f) };

        // the float array version of the clip's sample call, found by its exact parameters: the game's unity also has
        // span versions the mod's .net target can not compile against
        private static readonly System.Reflection.MethodInfo SetData = SampleCall("SetData");

        // each overload is looked at on its own: reading a span version's parameters fails where span is unknown
        private static System.Reflection.MethodInfo SampleCall(string name)
        {
            foreach (var method in typeof(AudioClip).GetMethods())
            {
                if (method.Name != name) continue;
                try
                {
                    var p = method.GetParameters();
                    if (p.Length == 2 && p[0].ParameterType == typeof(float[]) && p[1].ParameterType == typeof(int)) return method;
                }
                catch (System.Exception)
                {
                    // a span overload, not this one
                }
            }
            return null;
        }

        private static AudioClip _swell;
        private static bool _swellTried;

        private static float Master => Plugin.SlashSoundVolume.Value;

        // the swell, peaking the given seconds from now: started later, or part way in when the time is shorter
        internal static void Swell(Vector3 at, float untilPeak)
        {
            if (Master <= 0f || !LoadSwell()) return;
            var (_, mixer) = ThunderCharge.ClipOf(SwingPrefab, SwordClip);
            var holder = new GameObject("IaidoKatana_heavyswell");
            holder.transform.position = at;
            var source = Source(holder, _swell, mixer, 1f, SwellVolume);
            float skip = Mathf.Max(0f, SwellPeak - untilPeak);
            float wait = Mathf.Max(0f, untilPeak - SwellPeak);
            source.timeSamples = Mathf.Min((int)(skip * _swell.frequency), _swell.samples - 1);
            source.PlayScheduled(AudioSettings.dspTime + wait);
            Object.Destroy(holder, wait + _swell.length - skip + 0.2f);
        }

        // the swing, its loudest moment SwingPeak seconds after this call
        internal static void Swing(Vector3 at)
        {
            if (Master <= 0f) return;
            var (sword, mixer) = ThunderCharge.ClipOf(SwingPrefab, SwordClip);
            var (over, _) = ThunderCharge.ClipOf(SwingPrefab, OverClip);
            if (!sword && !over) return;
            var holder = new GameObject("IaidoKatana_heavyswing");
            holder.transform.position = at;
            double start = AudioSettings.dspTime + 0.02;
            float longest = 0f;
            foreach (var (tapAt, gain) in SwingTaps)
            {
                if (sword) Play(holder, sword, mixer, SwordPitch, SwordVolume * gain, SwordHead, start + tapAt);
                if (over) Play(holder, over, mixer, OverPitch, OverVolume * gain, OverHead, start + tapAt);
                longest = tapAt;
            }
            float length = Mathf.Max(sword ? sword.length / SwordPitch : 0f, over ? over.length / OverPitch : 0f);
            Object.Destroy(holder, longest + length + 0.3f);
        }

        private static void Play(GameObject holder, AudioClip clip, AudioMixerGroup mixer, float pitch, float volume, float head, double at)
        {
            var source = Source(holder, clip, mixer, pitch, volume);
            source.timeSamples = Mathf.Clamp((int)(head * clip.frequency), 0, clip.samples - 1);
            source.PlayScheduled(at);
        }

        private static AudioSource Source(GameObject holder, AudioClip clip, AudioMixerGroup mixer, float pitch, float volume)
        {
            var source = holder.AddComponent<AudioSource>();
            source.clip = clip;
            source.outputAudioMixerGroup = mixer;
            source.pitch = pitch;
            source.volume = Mathf.Min(1f, volume * Master);
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 3f;
            source.maxDistance = Falloff;
            source.dopplerLevel = 0f;
            return source;
        }

        // the shipped swell, read once from the dll (a 16 bit wav) into a clip
        private static bool LoadSwell()
        {
            if (_swellTried) return _swell;
            _swellTried = true;
            if (SetData == null)
            {
                Plugin.Log.LogWarning("steel heavy swell: the clip sample call was not found, no swell");
                return false;
            }
            byte[] wav;
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(SwellResource))
            {
                if (stream == null)
                {
                    Plugin.Log.LogWarning("steel heavy swell: the sound is missing from the dll, no swell");
                    return false;
                }
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    wav = memory.ToArray();
                }
            }
            int channels = 0, rate = 0, bits = 0, data = -1, size = 0;
            for (int at = 12; at + 8 <= wav.Length; at += 8 + size + (size & 1))
            {
                string id = System.Text.Encoding.ASCII.GetString(wav, at, 4);
                size = System.BitConverter.ToInt32(wav, at + 4);
                if (id == "fmt ")
                {
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
            if (data < 0 || bits != 16 || channels < 1 || rate <= 0)
            {
                Plugin.Log.LogWarning($"steel heavy swell: the sound could not be read ({channels} channels, {rate} Hz, {bits} bit), no swell");
                return false;
            }
            int count = Mathf.Min(size, wav.Length - data) / 2;
            var samples = new float[count];
            for (int i = 0; i < count; i++) samples[i] = System.BitConverter.ToInt16(wav, data + 2 * i) / 32768f;
            _swell = AudioClip.Create("IaidoKatana_heavyswell", count / channels, channels, rate, false);
            SetData.Invoke(_swell, new object[] { samples, 0 });
            if (Plugin.LogCounters.Value) Plugin.Log.LogInfo($"steel heavy swell: loaded ({_swell.length:0.00} s, {rate} Hz)");
            return true;
        }
    }
}
