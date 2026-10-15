using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
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
                // a full-body cut keeps its strike long enough to be seen (about 80 ms instead of 0.1.1's 45)
                bool full = !secondaryAttack && Moves.HasFullBody(ChainLevel(attack));
                if (full) CutShape.Apply(attack, ChainLevel(attack));
                SwingWarp.Begin(key,
                    secondaryAttack ? Plugin.HeavySpeed.Value : 1f,
                    secondaryAttack ? Plugin.HeavyStrikeBoost.Value : full ? Plugin.FullBodyStrikeBoost.Value : Plugin.NormalStrikeBoost.Value,
                    secondaryAttack ? Plugin.HeavyDuration.Value : 0f);
            }
        }

        // the katana's heavy with the blade already out plays its own clip on the greatsword's secondary state
        // (Swing.CustomAnimations), so the hand does not go back to the scabbard first. only this swing's copy of the attack
        // changes, the trigger reaches the other clients like any other
        [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
        private static class HeavyRoute
        {
            private static void Prefix(Attack __instance, Humanoid character, ItemDrop.ItemData weapon)
            {
                if (!(character is Player player) || player != Player.m_localPlayer || !Katana.IsKatana(weapon)) return;
                if (__instance.m_attackAnimation != ClipSwap.HeavyTrigger || !ClipSwap.HasHeavyOut || !ClipSwap.On(player) || !IaiStance.BladeOut(player)) return;
                __instance.m_attackAnimation = ClipSwap.HeavyOutTrigger;
                __instance.m_attackChainLevels = 0;     // the bare trigger, no chain or random number on it
                __instance.m_attackRandomAnimations = 0;
                if (Plugin.LogCounters.Value) Plugin.Log.LogInfo($"katana heavy with the blade out: plays on {ClipSwap.HeavyOutTrigger}");
            }
        }

        // the katana's own cuts hit where their blade goes (the blade's sweep measured from the clips with
        // devtools\session_2026-10-15b\hit_zone_laptop.py): the draw a sideways fan turned to the right, where it sweeps,
        // the overhead a fan from high to low, the diagonal the same a little thicker (the game's fan can not lean, so it
        // stands for the slant). 3 m of reach, the blade itself reaches about 2.4 m. only this swing's copy of the attack
        // changes
        internal static class CutShape
        {
            private struct Shape
            {
                internal Attack.AttackType Type;
                internal float Angle, Ray, Range, Yaw;
            }

            private static readonly Shape[] Shapes =
            {
                new Shape { Type = Attack.AttackType.Horizontal, Angle = 80f, Ray = 0.4f, Range = 3f, Yaw = 29f }, // blade -32..90 degrees
                new Shape { Type = Attack.AttackType.Vertical, Angle = 80f, Ray = 0.4f, Range = 3f, Yaw = 9f },    // down to 0.95 m at 7..15
                new Shape { Type = Attack.AttackType.Vertical, Angle = 80f, Ray = 0.5f, Range = 3f, Yaw = 10f },   // down to 0.99 m at 2..11
            };

            private static readonly ConditionalWeakTable<Attack, StrongBox<float>> Turns = new ConditionalWeakTable<Attack, StrongBox<float>>();

            internal static void Apply(Attack attack, int step)
            {
                if (step < 0 || step >= Shapes.Length) return;
                var shape = Shapes[step];
                attack.m_attackType = shape.Type;
                attack.m_attackAngle = shape.Angle;
                attack.m_attackRayWidth = shape.Ray;
                attack.m_attackRange = shape.Range;
                if (shape.Type == Attack.AttackType.Vertical)
                {
                    // the fan itself runs from high to low, no extra creature rays above and below
                    attack.m_attackHeightChar1 = 0f;
                    attack.m_attackHeightChar2 = 0f;
                }
                Turns.Remove(attack);
                Turns.Add(attack, new StrongBox<float>(shape.Yaw));
                if (Plugin.LogCounters.Value) Plugin.Log.LogInfo($"katana cut {step} hit area: {shape.Type} {shape.Angle:0} degrees, ray {shape.Ray} m, {shape.Range} m, turned {shape.Yaw:0} degrees right");
            }

            internal static float Turn(Attack attack) => Turns.TryGetValue(attack, out var box) ? box.Value : 0f;

            internal static int Count => Shapes.Length;
            internal static (float Angle, float Range, float Yaw) Of(int step) => (Shapes[step].Angle, Shapes[step].Range, Shapes[step].Yaw);
        }

        // the fan of a shaped cut is turned toward where its blade sweeps
        [HarmonyPatch(typeof(Attack), "GetMeleeAttackDir")]
        private static class CutTurn
        {
            private static void Postfix(Attack __instance, ref Vector3 attackDir)
            {
                float yaw = CutShape.Turn(__instance);
                if (yaw != 0f) attackDir = Quaternion.AngleAxis(yaw, Vector3.up) * attackDir;
            }
        }

        // the steel katana's heavy chops the trees, logs and stumps in its reach like the counter (the flametal's heavy is
        // the thunderclap, which chops along its dash)
        [HarmonyPatch(typeof(Attack), "DoMeleeAttack")]
        private static class HeavyChop
        {
            private static readonly AccessTools.FieldRef<Attack, Humanoid> Owner = AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");
            private static readonly AccessTools.FieldRef<Attack, ItemDrop.ItemData> Weapon = AccessTools.FieldRefAccess<Attack, ItemDrop.ItemData>("m_weapon");
            private static readonly AccessTools.FieldRef<Humanoid, bool> Secondary = AccessTools.FieldRefAccess<Humanoid, bool>("m_currentAttackIsSecondary");

            private static void Postfix(Attack __instance)
            {
                if (!(Owner(__instance) is Player player) || player != Player.m_localPlayer || !Secondary(player)) return;
                var weapon = Weapon(__instance);
                if (!Chop.On(weapon) || Plugin.IsFlametal(weapon)) return;
                var ahead = player.transform.forward;
                ahead.y = 0f;
                if (ahead.sqrMagnitude < 1e-4f) return;
                int chopped = Chop.CutAround(player, weapon, player.GetCenterPoint(), ahead.normalized, __instance.m_attackRange, Plugin.HeavyAngle.Value, Counter.AreaHeight);
                if (Plugin.LogCounters.Value)
                    Plugin.Log.LogInfo($"katana heavy chop: {chopped} trees, logs or stumps (chop {Chop.Damage(weapon):0}, tier {Chop.Tier(weapon)})");
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
            private static bool _wasPaused;
            private static Player _pausedPlayer;

            private static void Postfix(CharacterAnimEvent __instance, float __0)
            {
                if (!(AnimCharacter(__instance) is Player player) || player != Player.m_localPlayer) return;
                var animator = AnimAnimator(__instance);
                bool paused = PauseTimer(__instance) > 0f;
                if (player != _pausedPlayer) _wasPaused = false; // a new local player (a relog)
                if (paused && !_wasPaused) IaiStance.HitStop(player); // a landed swing froze the animator
                _wasPaused = paused;
                _pausedPlayer = player;
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
                ClipSwap.Step(__instance);
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
