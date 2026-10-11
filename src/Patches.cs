using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Iaido_Katana
{
    public static class Patches
    {
        private static readonly AccessTools.FieldRef<Humanoid, Attack> CurrentAttack = AccessTools.FieldRefAccess<Humanoid, Attack>("m_currentAttack");
        private static readonly AccessTools.FieldRef<Attack, int> ChainLevel = AccessTools.FieldRefAccess<Attack, int>("m_currentAttackCainLevel");
        private static readonly AccessTools.FieldRef<CharacterAnimEvent, Character> AnimCharacter = AccessTools.FieldRefAccess<CharacterAnimEvent, Character>("m_character");
        private static readonly AccessTools.FieldRef<CharacterAnimEvent, Animator> AnimAnimator = AccessTools.FieldRefAccess<CharacterAnimEvent, Animator>("m_animator");
        private static readonly AccessTools.FieldRef<CharacterAnimEvent, float> PauseTimer = AccessTools.FieldRefAccess<CharacterAnimEvent, float>("m_pauseTimer");

        // vanilla: parry = timedBlockBonus > 1 && m_blockTimer != -1 && m_blockTimer < 0.25f
        // patched: the 0.25f becomes Katana.ParryWindow(this), so only katanas get the shorter window
        [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
        private static class ParryWindowAndCounter
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var code = instructions.ToList();
                var timer = AccessTools.Field(typeof(Humanoid), "m_blockTimer");
                var window = AccessTools.Method(typeof(Katana), nameof(Katana.ParryWindow));

                var found = new List<int>();
                for (int i = 0; i < code.Count - 1; i++)
                    if (code[i].LoadsField(timer) && code[i + 1].opcode == OpCodes.Ldc_R4 && (float)code[i + 1].operand == Katana.VanillaParryWindow)
                        found.Add(i + 1);

                if (found.Count != 1)
                {
                    Plugin.Log.LogError($"could not find the parry window in Humanoid.BlockAttack ({found.Count} matches, game update?), katanas keep 250 ms");
                    return code;
                }

                int at = found[0];
                var loadThis = new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(code[at]);
                code[at] = new CodeInstruction(OpCodes.Call, window);
                code.Insert(at, loadThis);
                return code;
            }

            // decided before the block runs, with the same test the game uses
            private static void Prefix(Humanoid __instance, out bool __state) =>
                __state = __instance == Player.m_localPlayer && Katana.IsKatanaParry(__instance);

            private static void Postfix(Humanoid __instance, HitData hit, Character attacker, bool __result, bool __state)
            {
                if (__result && __state && attacker) Counter.Start((Player)__instance, attacker, hit.m_ranged);
            }
        }

        // a katana's normal block (not a perfect parry) wears it like its base sword's block would (Katana.BlockWearScale)
        [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
        private static class BlockWear
        {
            private static void Prefix(Humanoid __instance, out (ItemDrop.ItemData item, float before) __state)
            {
                var blocker = Katana.CurrentBlocker(__instance);
                __state = Katana.IsKatana(blocker) && !Katana.IsKatanaParry(__instance) ? (blocker, blocker.m_durability) : (null, 0f);
            }

            private static void Postfix((ItemDrop.ItemData item, float before) __state)
            {
                var item = __state.item;
                if (item == null || item.m_durability >= __state.before) return;
                float basePower = Katana.BaseBlockPower.TryGetValue(item.m_shared.m_name, out float power) ? power : 0f;
                float lost = __state.before - item.m_durability;
                float kept = lost * Katana.BlockWearScale(item.m_shared.m_blockPower, basePower);
                item.m_durability = __state.before - kept;
                if (Plugin.LogCounters.Value)
                    Plugin.Log.LogInfo($"block wear: {lost:0.00} by the game, {kept:0.00} like its base sword (block {item.m_shared.m_blockPower:0}, base {basePower:0})");
            }
        }

        // a katana swing starts, so the time-warp follows it
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        private static class WarpStart
        {
            // the flametal katana's heavy attack is the thunderclap, not a game attack
            private static bool Prefix(Humanoid __instance, bool secondaryAttack, ref bool __result)
            {
                if (!Thunderclap.TryStart(__instance, secondaryAttack)) return true;
                __result = false;
                return false;
            }

            private static void Postfix(Humanoid __instance, bool secondaryAttack, bool __result)
            {
                if (__instance != Player.m_localPlayer || !Katana.IsKatana(__instance.GetCurrentWeapon())) return;
                var attack = CurrentAttack(__instance);
                if (!__result || attack == null) return;
                if (Plugin.LogCounters.Value)
                    Plugin.Log.LogInfo($"katana {(secondaryAttack ? "heavy" : "normal")} swing started: animation {attack.m_attackAnimation}, chain {ChainLevel(attack)}");
                Counter.EndSwing();
                string key = $"{attack.m_attackAnimation}{(secondaryAttack ? "" : ChainLevel(attack).ToString())}";
                if (attack.m_attackAnimation.StartsWith(Plugin.UpperSwing))
                {
                    // on the upper body layer the legs keep walking: an even pace, and every client learns it is the heavy
                    if (secondaryAttack && __instance is Player player) IaiStance.StartHeavy(player);
                    SwingWarp.BeginEven(key, ChainLevel(attack), secondaryAttack ? Plugin.HeavyDuration.Value : 0f);
                    return;
                }
                SwingWarp.Begin(key,
                    secondaryAttack ? Plugin.HeavySpeed.Value : 1f,
                    secondaryAttack ? Plugin.HeavyStrikeBoost.Value : Plugin.NormalStrikeBoost.Value,
                    secondaryAttack ? Plugin.HeavyDuration.Value : 0f);
            }
        }

        // the hit moment of a swing, from the animation event. on the counter swing it lands the counter instead
        // of a weapon hit, so the counter swing can never hit twice
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnAttackTrigger))]
        private static class WarpHit
        {
            private static bool Prefix(Humanoid __instance)
            {
                if (__instance != Player.m_localPlayer) return true;
                if (Counter.AnimationPlaying && !SwingWarp.Active)
                {
                    Counter.OnSwingHit();
                    return false;
                }
                SwingWarp.OnHit();
                return true;
            }
        }

        // animation speed for the local player: the re-timed swing, or the fast placeholder counter swing
        [HarmonyPatch(typeof(CharacterAnimEvent), "CustomFixedUpdate")]
        private static class AnimationSpeed
        {
            private static void Postfix(CharacterAnimEvent __instance, float __0)
            {
                if (!(AnimCharacter(__instance) is Player player) || player != Player.m_localPlayer) return;
                var animator = AnimAnimator(__instance);
                bool paused = PauseTimer(__instance) > 0f;
                Counter.StepSwing(player, animator, paused);
                SwingWarp.Step(player, animator, __0, paused);
                Diagnostics.Step(player, animator);
            }
        }

        // the iai stance on every player with a katana, after the animator (it runs in the physics step) and before
        // LateUpdate: MagicaCloth reads the bones for the capes right before LateUpdate, so the stance has to be in place
        // by then, or a cape sees the plain pose on some frames and the stance on others and jiggles
        [HarmonyPatch(typeof(Player), "Update")]
        private static class Stance
        {
            private static void Postfix(Player __instance)
            {
                if (__instance == Player.m_localPlayer) Thunderclap.Update(__instance);
                IaiStance.Step(__instance);
            }
        }

        // put away with the blade out: the katana is sheathed first, the game puts it away after (IaiStance.StepSheathe)
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.HideHandItems))]
        private static class SheatheBeforePuttingAway
        {
            private static bool Prefix(Humanoid __instance, ref bool __result)
            {
                if (!(__instance is Player player) || player != Player.m_localPlayer || !IaiStance.DelayHide(player)) return true;
                __result = false;
                return false;
            }
        }

        // while the thunderclap gathers and cuts the player stands still, only a dodge gets through
        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        private static class ThunderControls
        {
            private static void Prefix(Player __instance, ref Vector3 movedir, ref bool attack, ref bool attackHold, ref bool secondaryAttack,
                ref bool secondaryAttackHold, ref bool block, ref bool blockHold, ref bool jump, ref bool crouch, ref bool run, ref bool autoRun, bool dodge) =>
                Thunderclap.Controls(__instance, ref movedir, ref attack, ref attackHold, ref secondaryAttack, ref secondaryAttackHold,
                    ref block, ref blockHold, ref jump, ref crouch, ref run, ref autoRun, dodge);
        }

        // the dash of the thunderclap can not be hit, like a dodge
        [HarmonyPatch(typeof(Player), nameof(Player.IsDodgeInvincible))]
        private static class ThunderInvincible
        {
            private static void Postfix(Player __instance, ref bool __result) => __result |= Thunderclap.Invincible(__instance);
        }

        // the dash moves the body after the game's own movement in each physics step (humanoids run the character's
        // step first, then their attacks)
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.CustomFixedUpdate))]
        private static class ThunderDash
        {
            private static void Postfix(Humanoid __instance) => Thunderclap.FixedStep(__instance);
        }

        // the game's speed changes inside a re-timed swing wait until its hit, then apply
        [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.Speed))]
        private static class IgnoreSpeedEvents
        {
            private static bool Prefix(CharacterAnimEvent __instance, float __0)
            {
                if (!SwingWarp.OverridesSpeedEvents || AnimCharacter(__instance) != Player.m_localPlayer) return true;
                SwingWarp.RecordGameSpeed(__0);
                return false;
            }
        }
    }
}
