using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace Iaido_Katana
{
    // the automatic strike back after a katana parry. with the counter swing on, the damage lands on the swing's
    // own hit moment, so the slash is seen before the number. without it, after a short delay
    internal static class Counter
    {
        private const float CounterPush = 5f;
        private const float SwingTimeout = 0.6f; // strike anyway if the swing's hit moment never comes

        private const float SwingMaxTime = 1f; // the counter swing never forces its speed longer than this

        private static readonly AccessTools.FieldRef<Character, ZSyncAnimation> ZAnim = AccessTools.FieldRefAccess<Character, ZSyncAnimation>("m_zanim");
        private static readonly AccessTools.FieldRef<Character, CharacterAnimEvent> AnimEvent = AccessTools.FieldRefAccess<Character, CharacterAnimEvent>("m_animEvent");
        private static bool _swinging;
        private static bool _swingSeen;
        private static float _swingStart;

        private static Player _player;
        private static Character _attacker;
        private static bool _ranged;
        private static float _parryTime;
        private static int _id;

        // true from the parry until the counter swing has fully played out, recovery included
        internal static bool AnimationPlaying => _swinging;

        // runs every fixed step for the local player. the whole swing plays fast, so the guard comes back quickly
        internal static void StepSwing(Player player, Animator animator, bool paused)
        {
            if (!_swinging) return;
            if (Time.time - _swingStart > SwingMaxTime || (!player.InAttack() && (_swingSeen || Time.time - _swingStart > 0.3f)))
            {
                EndSwing();
                return;
            }
            if (player.InAttack()) _swingSeen = true;
            if (!paused) animator.speed = Plugin.CounterAnimationSpeed.Value;
        }

        // a real swing took over, or the counter swing is done
        internal static void EndSwing() => _swinging = false;

        internal static void Start(Player player, Character attacker, bool ranged)
        {
            if (!Plugin.CounterEnabled.Value || !attacker) return;
            if (attacker.IsPlayer() && !Plugin.CounterInPvP.Value) return;

            _player = player;
            _attacker = attacker;
            _ranged = ranged;
            _parryTime = Time.time;
            _id++;
            // from the iai stance: the draw and cut, the counter lands as the blade passes the middle
            if (IaiStance.SlashOn && IaiStance.HoldsKatana(player))
            {
                IaiStance.StartSlash(player);
                player.StartCoroutine(StrikeAfter(_id, Mathf.Max(Plugin.CounterDelay.Value, IaiSlash.HitTime)));
                return;
            }
            bool swing = Plugin.CounterAnimation.Value && PlaySwing(player);
            player.StartCoroutine(StrikeAfter(_id, swing ? SwingTimeout : Plugin.CounterDelay.Value));
        }

        // the counter swing reached its hit moment
        internal static void OnSwingHit()
        {
            if (!_player) return;
            // the game only doubles damage on a staggered target once the parry stagger shows in its animator
            float wait = Plugin.CounterDelay.Value - (Time.time - _parryTime);
            if (wait > 0f) _player.StartCoroutine(StrikeAfter(_id, wait));
            else Strike(_id);
        }

        // placeholder until the iai animation exists: the katana's first swing, played fast
        private static bool PlaySwing(Player player)
        {
            var attack = player.GetCurrentWeapon()?.m_shared.m_attack;
            if (attack == null || string.IsNullOrEmpty(attack.m_attackAnimation)) return false;
            ZAnim(player).SetTrigger(attack.m_attackChainLevels > 1 ? attack.m_attackAnimation + "0" : attack.m_attackAnimation);
            _swinging = true;
            _swingSeen = false;
            _swingStart = Time.time;
            return true;
        }

        // what the game's freeze message does on each client, done here only
        internal static void LocalFreeze(Character character, float seconds)
        {
            var animEvent = seconds > 0f && character ? AnimEvent(character) : null;
            if (animEvent) animEvent.FreezeFrame(seconds);
        }

        private static IEnumerator StrikeAfter(int id, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            Strike(id);
        }

        // one strike per parry: whichever comes first, the swing's hit moment or the timeout, uses it up
        private static void Strike(int id)
        {
            if (id != _id) return; // a newer parry took over
            var player = _player;
            var attacker = _attacker;
            bool ranged = _ranged;
            _player = null;
            _attacker = null;
            if (!player || player.IsDead() || !attacker || attacker.IsDead()) return;
            var weapon = player.GetCurrentWeapon();
            if (!Katana.IsKatana(weapon)) return;

            float factor = Katana.CounterFactor(player.GetRandomSkillFactor(Skills.SkillType.Swords),
                player.GetSkillFactor(Skills.SkillType.Blocking), ranged,
                Plugin.CounterMultiplier.Value, Plugin.BlockBonusMax.Value, Plugin.ReflectMultiplier.Value)
                * Katana.PreciseFactor(Plugin.ParryPrecise.Value, Plugin.ParryPreciseBonus.Value);

            var hit = new HitData();
            hit.m_damage = weapon.GetDamage();
            hit.m_damage.Modify(factor);
            hit.m_point = attacker.GetCenterPoint();
            hit.m_dir = (hit.m_point - player.GetCenterPoint()).normalized;
            hit.m_attacker = player.GetZDOID();
            hit.m_skill = Skills.SkillType.Swords;
            hit.m_hitType = HitData.HitType.PlayerHit;
            hit.m_toolTier = (short)weapon.m_shared.m_toolTier;
            hit.m_pushForce = CounterPush;
            hit.m_blockable = false;
            hit.m_dodgeable = false;
            attacker.Damage(hit);

            KatanaFx.HitEffect(weapon.m_shared.m_hitEffect, hit.m_point, Quaternion.LookRotation(hit.m_dir), ImpactFrame.InkHides);
            // the game's own hit stop on both fighters, longer than a normal hit's (Impact.FreezeSeconds). only on this
            // client: Character.FreezeFrame would send it to every player
            LocalFreeze(player, Plugin.ImpactFreeze.Value);
            LocalFreeze(attacker, Plugin.ImpactFreeze.Value);
            IaiStance.HoldSlash(player, Plugin.ImpactFreeze.Value);
            ImpactFrame.Play(hit.m_point);
            player.RaiseSkill(Skills.SkillType.Swords, 1f);

            if (Plugin.LogCounters.Value)
                Plugin.Log.LogInfo($"counter on {attacker.m_name}: {hit.m_damage.GetTotalDamage():0} before resistances and stagger (x{factor:0.00} of the katana{(ranged ? ", reflected" : "")}{(Plugin.ParryPrecise.Value ? ", precise" : "")}, {Time.time - _parryTime:0.00} s after the parry)");
        }
    }
}
