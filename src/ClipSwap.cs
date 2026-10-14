using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace Iaido_Katana
{
    // the katana's own animation clips, made in Unity (Tools\Modding\UnityAnim) and carried inside the dll as an asset
    // bundle. with Swing.CustomAnimations on, every player's animator plays them in place of the game's clips they stand
    // in for while that player holds a katana, through an override of the game's own controller, so friends with the
    // mod see the same. greatswords and sledges keep the game's clips
    internal static class ClipSwap
    {
        private const string Resource = "Iaido_Katana.iaido_anims";
        private static readonly AccessTools.FieldRef<Character, Animator> AnimatorOf = AccessTools.FieldRefAccess<Character, Animator>("m_animator");

        internal const string HeavyTrigger = "swing_sledge";            // the heavy's state, its clip drawn from the scabbard
        internal const string HeavyOutTrigger = "greatsword_secondary"; // the state the heavy uses when the blade is already out
        private const string HeavyOutClip = "Iaido Heavy Out";

        // the game's clip each of ours stands in for
        private static readonly Dictionary<string, string> Replaces = new Dictionary<string, string>
        {
            ["Greatsword BaseAttack (1)"] = "Iaido Draw",           // cut 0, the draw cut out of the scabbard
            ["Greatsword BaseAttack (2)"] = "Iaido Overhead",       // cut 1, the overhead from the draw's end
            ["Greatsword BaseAttack (3)"] = "Iaido Diagonal",       // cut 2, the downward diagonal from the overhead's end
            ["Sledge-Attack1"] = "Iaido Heavy",                     // the heavy, drawn up from the scabbard
            ["Greatsword Secondary Attack"] = HeavyOutClip,         // the heavy with the blade already out
        };

        private static readonly Dictionary<string, AnimationClip> Clips = new Dictionary<string, AnimationClip>();
        private static readonly ConditionalWeakTable<Animator, Swap> Swaps = new ConditionalWeakTable<Animator, Swap>();
        private static bool _loaded;

        private sealed class Swap
        {
            internal RuntimeAnimatorController Original;
            internal AnimatorOverrideController Override;
        }

        // one of our clips is playing (it is a swing like the game's own)
        internal static bool IsOwn(string clipName) => Clips.ContainsKey(clipName);

        internal static bool HasHeavyOut => Clips.ContainsKey(HeavyOutClip);

        // this player's animator plays our clips now
        internal static bool On(Player player)
        {
            var animator = player ? AnimatorOf(player) : null;
            return animator && Swaps.TryGetValue(animator, out var swap) && swap.Override && animator.runtimeAnimatorController == swap.Override;
        }

        // the combo step (0 to 2) or the heavy (Moves.HeavyStep) one of our clips plays, from the game clip it stands in for
        internal static int StepOf(string clipName)
        {
            foreach (var pair in Replaces)
            {
                if (pair.Value != clipName) continue;
                string game = pair.Key;
                if (game.StartsWith("Greatsword BaseAttack (") && game.Length == "Greatsword BaseAttack (".Length + 2) return game["Greatsword BaseAttack (".Length] - '1';
                return game == "Greatsword Secondary Attack" ? Moves.HeavyOutStep : Moves.HeavyStep;
            }
            return -1;
        }

        // the bundle out of the dll, once at start. a build without it simply has no own clips
        internal static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource))
            {
                if (stream == null) return;
                var bytes = new byte[stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = stream.Read(bytes, read, bytes.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                var bundle = AssetBundle.LoadFromMemory(bytes);
                if (!bundle)
                {
                    Plugin.Log.LogWarning("own animation clips: the bundle did not load (made for another Unity version?)");
                    return;
                }
                foreach (var clip in bundle.LoadAllAssets<AnimationClip>()) Clips[clip.name] = clip;
                Plugin.Log.LogInfo($"own animation clips: {Clips.Count} loaded ({string.Join(", ", Clips.Keys)})");
            }
        }

        // every frame for every player in view: our clips while the player holds a katana with Swing.CustomAnimations
        // on (and not the upper body swings), the game's otherwise. the override is made once per animator and then
        // switched. only our own override is ever taken off, a controller another mod put on stays
        internal static void Step(Player player)
        {
            if (Clips.Count == 0 || !player) return;
            var animator = AnimatorOf(player);
            if (!animator || !animator.isInitialized) return;
            bool want = Moves.FullBody && IaiStance.HoldsKatana(player);
            var current = animator.runtimeAnimatorController;
            Swaps.TryGetValue(animator, out var swap);
            bool on = swap != null && swap.Override && current == swap.Override;
            if (want == on) return;
            if (!want)
            {
                Assign(animator, swap.Original);
                return;
            }
            if (!current) return;
            if (swap == null || current != swap.Original)
            {
                if (swap != null) Swaps.Remove(animator);
                swap = new Swap { Original = current, Override = Build(current, out int replaced) };
                Swaps.Add(animator, swap);
                if (Plugin.LogCounters.Value) Plugin.Log.LogInfo($"own animation clips for {player.GetPlayerName()}: {replaced} of the game's clips replaced");
            }
            if (swap.Override) Assign(animator, swap.Override); // none when the controller has none of the clips
        }

        // the game's controller with our clips in place of the ones they stand in for, or null when it has none of them
        private static AnimatorOverrideController Build(RuntimeAnimatorController original, out int replaced)
        {
            var over = new AnimatorOverrideController(original) { name = "IaidoKatana_clips" };
            var list = new List<KeyValuePair<AnimationClip, AnimationClip>>(over.overridesCount);
            over.GetOverrides(list);
            replaced = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var clip = list[i].Key;
                if (!clip || !Replaces.TryGetValue(clip.name, out var own) || !Clips.TryGetValue(own, out var mine)) continue;
                list[i] = new KeyValuePair<AnimationClip, AnimationClip>(clip, mine);
                replaced++;
            }
            if (replaced == 0) return null;
            over.ApplyOverrides(list);
            return over;
        }

        // a new controller resets the animator's parameters and starts every layer over, so the parameters are carried
        // over (the weapon's idle and walk, the speeds, the flags the game set once) and each layer goes on in the state,
        // time and weight it had (equipping the katana mid-walk does not snap)
        private static void Assign(Animator animator, RuntimeAnimatorController controller)
        {
            int layers = animator.layerCount;
            var states = new int[layers];
            var times = new float[layers];
            var weights = new float[layers];
            for (int i = 0; i < layers; i++)
            {
                var info = animator.GetCurrentAnimatorStateInfo(i);
                states[i] = info.fullPathHash;
                times[i] = info.normalizedTime;
                weights[i] = animator.GetLayerWeight(i);
            }
            var parameters = animator.parameters;
            var floats = new float[parameters.Length];
            var ints = new int[parameters.Length];
            var bools = new bool[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                if (p.type == AnimatorControllerParameterType.Float) floats[i] = animator.GetFloat(p.nameHash);
                else if (p.type == AnimatorControllerParameterType.Int) ints[i] = animator.GetInteger(p.nameHash);
                else if (p.type == AnimatorControllerParameterType.Bool) bools[i] = animator.GetBool(p.nameHash);
            }
            animator.runtimeAnimatorController = controller;
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                if (p.type == AnimatorControllerParameterType.Float) animator.SetFloat(p.nameHash, floats[i]);
                else if (p.type == AnimatorControllerParameterType.Int) animator.SetInteger(p.nameHash, ints[i]);
                else if (p.type == AnimatorControllerParameterType.Bool) animator.SetBool(p.nameHash, bools[i]);
            }
            for (int i = 0; i < layers && i < animator.layerCount; i++)
            {
                animator.SetLayerWeight(i, weights[i]);
                if (states[i] != 0) animator.Play(states[i], i, times[i]);
            }
        }
    }
}
