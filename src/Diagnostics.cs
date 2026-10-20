using System.Collections.Generic;
using System;
using System.Linq;
using UnityEngine;

namespace Iaido_Katana
{
    // with Debug.DumpAnimationClips on: lists the player animator's clips and parameters once, then logs which clips
    // play on every layer whenever that changes while a katana is held. these are the names an
    // AnimatorOverrideController needs to swap in the iai stance and slash
    internal static class Diagnostics
    {
        private static bool _dumped;
        private static string _lastPlaying;
        private static float _attackSince = -1f;
        private static int _longAttackLogs;
        private const int HistoryLimit = 64;
        private const float SnapshotInterval = 5f;
        private static readonly Queue<string> History = new Queue<string>();
        private static float _lastSnapshot = -SnapshotInterval;
        private static Player _watchPlayer;

        internal static void Remember(string message)
        {
            if (Plugin.LogCounters?.Value != true) return;
            if (History.Count >= HistoryLimit) History.Dequeue();
            History.Enqueue($"{DateTime.UtcNow:O} {message}");
        }

        internal static void Record(string message)
        {
            if (Plugin.LogCounters?.Value != true) return;
            Remember(message);
            Plugin.Log.LogInfo($"katana safety: {message}");
        }

        internal static void Snapshot(string why, Player player, Animator animator)
        {
            if (Plugin.LogCounters?.Value != true) return;
            Record(why);
            if (Time.time - _lastSnapshot < SnapshotInterval) return;
            _lastSnapshot = Time.time;
            if (player && animator && animator.isInitialized)
            {
                int layer = AnimationSafety.AttackLayer(animator);
                var state = animator.GetCurrentAnimatorStateInfo(layer);
                var next = animator.IsInTransition(layer) ? animator.GetNextAnimatorStateInfo(layer) : default;
                var clips = animator.GetCurrentAnimatorClipInfo(layer);
                string clip = clips.Length > 0 && clips[0].clip ? clips[0].clip.name : "none";
                Plugin.Log.LogInfo($"katana snapshot: layer {layer}, clip {clip}, state {state.fullPathHash} at {state.normalizedTime:0.000}, " +
                    $"next {next.fullPathHash} at {next.normalizedTime:0.000}, speed {animator.speed:0.0000}, weapon {player.GetCurrentWeapon()?.m_shared.m_name ?? "none"}, " +
                    $"own clips {ClipSwap.On(player)}, warp {SwingWarp.Active}, counter {Counter.AnimationPlaying}");
            }
            foreach (string line in History) Plugin.Log.LogInfo($"katana history: {line}");
        }

        // Debug.LogCounters: the local player stays in an attack animation far longer than any swing. logged at 4 s and
        // again at 12 s, with what could be holding the animator
        internal static void Watch(Player player, Animator animator)
        {
            if (!Plugin.LogCounters.Value || !animator) return;
            if (_watchPlayer != player)
            {
                _watchPlayer = player;
                _attackSince = -1f;
                _longAttackLogs = 0;
                History.Clear();
                _lastSnapshot = -SnapshotInterval;
            }
            if (!player.InAttack())
            {
                _attackSince = -1f;
                _longAttackLogs = 0;
                return;
            }
            if (_attackSince < 0f) _attackSince = Time.time;
            float held = Time.time - _attackSince;
            if (_longAttackLogs >= 2 || held < (_longAttackLogs == 0 ? 4f : 12f)) return;
            _longAttackLogs++;
            Snapshot($"attack flag held for {held:0.0} s", player, animator);
            var clips = animator.GetCurrentAnimatorClipInfo(0);
            string clip = clips.Length > 0 && clips[0].clip ? clips[0].clip.name : "none";
            Plugin.Log.LogInfo($"in an attack animation for {held:0.0} s: clip {clip}, in transition {animator.IsInTransition(0)}, animator speed {animator.speed:0.00}, " +
                               $"weapon {player.GetCurrentWeapon()?.m_shared.m_name ?? "none"}, swing warp {SwingWarp.Active}, counter swing {Counter.AnimationPlaying}, own clips {ClipSwap.On(player)}");
        }

        internal static void Step(Player player, Animator animator)
        {
            if (!Plugin.DumpAnimationClips.Value || !animator || !animator.runtimeAnimatorController) return;

            if (!_dumped)
            {
                _dumped = true;
                var controller = animator.runtimeAnimatorController;
                var clips = controller.animationClips.Where(c => c).Select(c => c.name).Distinct().OrderBy(n => n).ToList();
                Plugin.Log.LogInfo($"player animator '{controller.name}': {animator.layerCount} layers, {clips.Count} clips, " +
                                   $"{(animator.isHuman ? "humanoid rig (animations can be retargeted)" : "generic rig (animations must be made on this skeleton)")}, avatar '{(animator.avatar ? animator.avatar.name : "none")}'");
                for (int i = 0; i < animator.layerCount; i++) Plugin.Log.LogInfo($"  layer {i}: {animator.GetLayerName(i)}");
                Plugin.Log.LogInfo("  clips: " + string.Join(", ", clips));
                Plugin.Log.LogInfo("  parameters: " + string.Join(", ", animator.parameters.Select(p => $"{p.name} ({p.type})")));
            }

            if (!Katana.IsKatana(player.GetCurrentWeapon())) return;
            var playing = new List<string>();
            for (int i = 0; i < animator.layerCount; i++)
            {
                if (animator.GetLayerWeight(i) <= 0.01f && i > 0) continue;
                var names = animator.GetCurrentAnimatorClipInfo(i).Where(c => c.clip).Select(c => c.clip.name);
                playing.Add($"{i}:{string.Join("+", names)}");
            }
            string now = string.Join(" | ", playing) + (player.IsBlocking() ? " [blocking]" : "") + (player.InAttack() ? " [attack]" : "");
            if (now == _lastPlaying) return;
            _lastPlaying = now;
            Plugin.Log.LogInfo("playing: " + now);
        }
    }
}
