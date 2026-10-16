using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;

namespace Iaido_Katana
{
    // a katana's block clank plays quieter when it parries (Impact.ParryClankVolume). during a katana parry the katana's
    // block effect is swapped for a copy whose sounds are clones with a lower volume. the clones are network prefabs,
    // so the clank is spawned like the game's own and every player with the mod hears it
    internal static class ParryClank
    {
        private const string Suffix = "_iaidoparry";
        private static readonly Dictionary<string, EffectList> ParryEffects = new Dictionary<string, EffectList>(); // by the katana's name token
        private static readonly Dictionary<string, GameObject> Clones = new Dictionary<string, GameObject>();
        private static readonly Dictionary<ZSFX, Vector2> BaseVolume = new Dictionary<ZSFX, Vector2>();
        private static ItemDrop.ItemData.SharedData _swapped;
        private static EffectList _original;

        // made once per katana when the katanas are made, from its block effect (the base sword's)
        internal static void Prepare(ItemDrop.ItemData.SharedData shared, string token)
        {
            var source = shared.m_blockEffect?.m_effectPrefabs;
            if (source == null) return;
            var copies = new EffectList.EffectData[source.Length];
            int sounds = 0;
            for (int i = 0; i < source.Length; i++)
            {
                var data = source[i];
                copies[i] = new EffectList.EffectData
                {
                    m_prefab = data.m_prefab, m_enabled = data.m_enabled, m_variant = data.m_variant, m_attach = data.m_attach,
                    m_follow = data.m_follow, m_inheritParentRotation = data.m_inheritParentRotation, m_inheritParentScale = data.m_inheritParentScale,
                    m_multiplyParentVisualScale = data.m_multiplyParentVisualScale, m_randomRotation = data.m_randomRotation,
                    m_scale = data.m_scale, m_childTransform = data.m_childTransform
                };
                if (!data.m_prefab || !data.m_prefab.GetComponentInChildren<ZSFX>(true)) continue;
                var quiet = Quieter(data.m_prefab);
                if (quiet == data.m_prefab) continue;
                copies[i].m_prefab = quiet;
                sounds++;
            }
            ParryEffects[token] = new EffectList { m_effectPrefabs = copies };
            if (sounds == 0) Plugin.Log.LogWarning($"{token}: no block sound found, its parry clank stays the game's");
        }

        // a clone of a sound prefab with its volume scaled, made once and registered as a network prefab
        private static GameObject Quieter(GameObject sound)
        {
            string name = sound.name + Suffix;
            if (Clones.TryGetValue(name, out var made)) return made;
            var clone = PrefabManager.Instance.CreateClonedPrefab(name, sound);
            if (!clone) return sound;
            foreach (var sfx in clone.GetComponentsInChildren<ZSFX>(true)) BaseVolume[sfx] = new Vector2(sfx.m_minVol, sfx.m_maxVol);
            PrefabManager.Instance.AddPrefab(clone);
            Clones[name] = clone;
            ApplyVolume();
            return clone;
        }

        // the clones' volumes from the setting, against the game's own sound
        internal static void ApplyVolume()
        {
            float k = Plugin.ParryClankVolume.Value;
            foreach (var pair in BaseVolume)
            {
                if (!pair.Key) continue;
                pair.Key.m_minVol = pair.Value.x * k;
                pair.Key.m_maxVol = pair.Value.y * k;
            }
        }

        // during a katana parry its block effect is the quieter copy, put back after the block
        internal static void Swap(ItemDrop.ItemData blocker)
        {
            if (Plugin.ParryClankVolume.Value >= 0.999f || blocker?.m_shared == null) return;
            if (!ParryEffects.TryGetValue(blocker.m_shared.m_name, out var quiet)) return;
            _swapped = blocker.m_shared;
            _original = _swapped.m_blockEffect;
            _swapped.m_blockEffect = quiet;
            if (Plugin.LogCounters.Value) Plugin.Log.LogInfo($"katana parry: block clank at {Plugin.ParryClankVolume.Value:0.##} of the game's");
        }

        internal static void Restore()
        {
            if (_swapped != null) _swapped.m_blockEffect = _original;
            _swapped = null;
            _original = null;
        }
    }
}
