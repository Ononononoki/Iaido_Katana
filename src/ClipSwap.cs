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
        private const float CheckInterval = 1f;

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
            internal int Replaced;
            internal float CheckAt;
            internal readonly List<KeyValuePair<AnimationClip, AnimationClip>> Buffer = new List<KeyValuePair<AnimationClip, AnimationClip>>();
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
        internal static void Step(Player player, bool validate = false)
        {
            if (Clips.Count == 0 || !player) return;
            var animator = AnimatorOf(player);
            if (!animator || !animator.isInitialized)
            {
                Trace(player, "animator not ready", false, false, null, null);
                return;
            }
            bool want = Moves.FullBody && IaiStance.HoldsKatana(player);
            var current = animator.runtimeAnimatorController;
            Swaps.TryGetValue(animator, out var swap);
            bool on = swap != null && swap.Override && current == swap.Override;
            if (want != on && (player.InAttack() || AnimationSafety.Attacking(animator)))
            {
                Trace(player, "controller change deferred until attack ends", want, on, current, swap);
                return;
            }
            Trace(player, "checked", want, on, current, swap);
            if (want && on && (validate || Time.time >= swap.CheckAt)
                && !player.InAttack() && !AnimationSafety.Attacking(animator))
            {
                swap.CheckAt = Time.time + CheckInterval;
                int repaired = Repair(swap);
                if (repaired > 0) Diagnostics.Snapshot($"repaired {repaired} custom animation mappings before an attack", player, animator);
            }
            if (want == on) return;
            if (!want)
            {
                Assign(animator, swap.Original);
                return;
            }
            if (!current)
            {
                Trace(player, "no controller to build on", want, on, current, swap);
                return;
            }
            bool invalid = swap != null && swap.Replaced > 0 && !swap.Override;
            if (NeedsRebuild(swap, current))
            {
                if (invalid) Diagnostics.Snapshot("rebuilding an invalid animation override", player, animator);
                if (swap != null) Swaps.Remove(animator);
                swap = new Swap { Original = current, Override = Build(current, out int replaced) };
                swap.Replaced = replaced;
                Swaps.Add(animator, swap);
                if (Plugin.LogCounters.Value) Plugin.Log.LogInfo($"own animation clips for {player.GetPlayerName()}: {replaced} of the game's clips replaced");
            }
            if (swap.Override) Assign(animator, swap.Override); // none when the controller has none of the clips
        }

        private static bool NeedsRebuild(Swap swap, RuntimeAnimatorController current) =>
            swap == null || current != swap.Original || swap.Replaced > 0 && !swap.Override;

        // repair only our clip slots while preserving every unrelated replacement
        private static int Repair(Swap swap)
        {
            swap.Override.GetOverrides(swap.Buffer);
            int repaired = 0;
            for (int i = 0; i < swap.Buffer.Count; i++)
            {
                var pair = swap.Buffer[i];
                if (!pair.Key || !Replaces.TryGetValue(pair.Key.name, out string name)
                    || !Clips.TryGetValue(name, out var own) || !own || pair.Value == own) continue;
                swap.Buffer[i] = new KeyValuePair<AnimationClip, AnimationClip>(pair.Key, own);
                repaired++;
            }
            if (repaired > 0) swap.Override.ApplyOverrides(swap.Buffer);
            return repaired;
        }

        private static bool _traceSeen;
        private static string _traceWhy;
        private static bool _traceWant;
        private static bool _traceOn;
        private static bool _traceOriginal;
        private static bool _traceAlive;
        private static RuntimeAnimatorController _traceController;
        private static int _traceLines;
        private const int TraceLimit = 300;
        private const float TraceWindow = 60f;
        private static float _traceSince;

        // Debug.LogCounters: the local player's override state, logged each time any part of it changes (capped, so a
        // state that flips every frame cannot flood the log)
        private static void Trace(Player player, string why, bool want, bool on, RuntimeAnimatorController current, Swap swap)
        {
            if (!Plugin.LogCounters.Value || player != Player.m_localPlayer) return;
            bool original = swap != null && current == swap.Original;
            bool alive = swap != null && swap.Override;
            if (_traceSeen && _traceWhy == why && _traceWant == want && _traceOn == on && _traceOriginal == original && _traceAlive == alive && _traceController == current) return;
            _traceSeen = true;
            _traceWhy = why;
            _traceWant = want;
            _traceOn = on;
            _traceOriginal = original;
            _traceAlive = alive;
            _traceController = current;
            string message = $"clip swap: {why}, katana held {want}, own clips on {on}, controller {(current ? current.name : "none")}{(original ? " (the game's)" : "")}, own override {(swap == null ? "not built" : alive ? "alive" : "destroyed")}";
            Diagnostics.Remember(message);
            if (Time.time - _traceSince >= TraceWindow)
            {
                _traceSince = Time.time;
                _traceLines = 0;
            }
            if (++_traceLines > TraceLimit)
            {
                if (_traceLines == TraceLimit + 1) Plugin.Log.LogInfo($"clip swap: {TraceLimit} state lines this minute, recent history still retained");
                return;
            }
            Plugin.Log.LogInfo(message);
        }

        // the game's controller with our clips in place of the ones they stand in for, or null when it has none of them
        private static AnimatorOverrideController Build(RuntimeAnimatorController original, out int replaced)
        {
            var over = new AnimatorOverrideController(original) { name = "IaidoKatana_clips" };
            var list = new List<KeyValuePair<AnimationClip, AnimationClip>>(over.overridesCount);
            if (original is AnimatorOverrideController inherited) inherited.GetOverrides(list);
            else over.GetOverrides(list);
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

        // assigning an override preserves state, but restoring a plain controller requires replaying the layers
        private static RuntimeAnimatorController BaseOf(RuntimeAnimatorController controller)
        {
            while (controller is AnimatorOverrideController over) controller = over.runtimeAnimatorController;
            return controller;
        }

        private static void Assign(Animator animator, RuntimeAnimatorController controller)
        {
            if (controller is AnimatorOverrideController && BaseOf(animator.runtimeAnimatorController) == BaseOf(controller))
            {
                animator.runtimeAnimatorController = controller;
                return;
            }
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
