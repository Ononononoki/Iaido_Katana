using System.Collections.Generic;
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
