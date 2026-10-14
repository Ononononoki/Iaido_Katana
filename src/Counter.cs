using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
        // enemies parried while a counter was still coming, cut by the same counter
        private static readonly List<(Character Who, bool Reflected)> AlsoParried = new List<(Character, bool)>();
        // when each enemy last took a hit of the counters: one per enemy within CutAgain seconds
        private static readonly Dictionary<Character, float> CutAt = new Dictionary<Character, float>();
        private const float CutAgain = 0.25f;
        private const float GatherFor = 1f; // a counter still waiting this long after its parry is stale

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

            // another enemy parried while this player's counter is still coming: the same cut gives it its own counter,
            // so parries at the same moment never add their cuts up
            if (_player == player && _attacker && attacker != _attacker && Time.time - _parryTime < GatherFor)
            {
                if (!AlsoParried.Exists(p => p.Who == attacker)) AlsoParried.Add((attacker, ranged));
                return;
            }
            AlsoParried.Clear();
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

            float Factor(bool reflected) => Katana.CounterFactor(player.GetRandomSkillFactor(Skills.SkillType.Swords),
                player.GetSkillFactor(Skills.SkillType.Blocking), reflected,
                Plugin.CounterMultiplier.Value, Plugin.BlockBonusMax.Value, Plugin.ReflectMultiplier.Value)
                * Katana.PreciseFactor(Plugin.ParryPrecise.Value, Plugin.ParryPreciseBonus.Value);
            float factor = Factor(ranged);

            var hit = Hit(player, attacker, weapon, factor);
            attacker.Damage(hit);
            CutAt[attacker] = Time.time;

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

            // the others parried while this counter was coming: the same cut gives each its own counter
            bool melee = !ranged;
            foreach (var (who, reflected) in AlsoParried)
            {
                if (!who || who.IsDead()) continue;
                float f = Factor(reflected);
                var also = Hit(player, who, weapon, f);
                who.Damage(also);
                CutAt[who] = Time.time;
                KatanaFx.HitEffect(weapon.m_shared.m_hitEffect, also.m_point, Quaternion.LookRotation(also.m_dir), ImpactFrame.InkHides);
                LocalFreeze(who, Plugin.ImpactFreeze.Value);
                melee |= !reflected;
                if (Plugin.LogCounters.Value)
                    Plugin.Log.LogInfo($"counter on {who.m_name}, parried with it: {also.m_damage.GetTotalDamage():0} before resistances and stagger (x{f:0.00} of the katana{(reflected ? ", reflected" : "")})");
            }
            AlsoParried.Clear();
            if (melee && Plugin.CounterArea.Value) CutAround(player, attacker, weapon);
        }

        // one hit of the counter's cut, from the player toward the target
        private static HitData Hit(Player player, Character target, ItemDrop.ItemData weapon, float factor)
        {
            var hit = new HitData();
            hit.m_damage = weapon.GetDamage();
            hit.m_damage.Modify(factor);
            hit.m_point = target.GetCenterPoint();
            hit.m_dir = (hit.m_point - player.GetCenterPoint()).normalized;
            hit.m_attacker = player.GetZDOID();
            hit.m_skill = Skills.SkillType.Swords;
            hit.m_hitType = HitData.HitType.PlayerHit;
            hit.m_toolTier = (short)weapon.m_shared.m_toolTier;
            hit.m_pushForce = CounterPush;
            hit.m_blockable = false;
            hit.m_dodgeable = false;
            return hit;
        }

        // 0.2.1 (the user: the counter was weak in hordes): the counter's cut also takes every other enemy within a
        // normal swing toward the parried attacker, each with a full hit of AreaDamageMultiplier (no share between
        // targets, like the katana's own swings). an enemy cut by a counter in the last CutAgain seconds is left out,
        // so parries close together never add their cuts up on one enemy
        private static void CutAround(Player player, Character attacker, ItemDrop.ItemData weapon)
        {
            var from = player.GetCenterPoint();
            var ahead = attacker.transform.position - player.transform.position;
            ahead.y = 0f;
            ahead = ahead.sqrMagnitude > 1e-4f ? ahead.normalized : player.transform.forward;
            foreach (var gone in CutAt.Where(c => !c.Key || Time.time - c.Value > CutAgain).Select(c => c.Key).ToList()) CutAt.Remove(gone);
            foreach (var who in Character.GetAllCharacters())
            {
                if (!who || who == player || who.IsDead() || CutAt.ContainsKey(who) || !Thunderclap.Enemy(player, who)) continue;
                var point = who.GetCenterPoint();
                if (!Katana.InSwing(point - from, ahead, who.GetRadius(), Plugin.NormalRange.Value, Plugin.NormalAngle.Value)) continue;
                if (Physics.Linecast(from, point, Thunderclap.Walls)) continue; // not through walls and rocks

                var hit = Hit(player, who, weapon, Plugin.CounterAreaMultiplier.Value * player.GetRandomSkillFactor(Skills.SkillType.Swords));
                who.Damage(hit);
                CutAt[who] = Time.time;
                KatanaFx.HitEffect(weapon.m_shared.m_hitEffect, point, Quaternion.LookRotation(hit.m_dir), ImpactFrame.InkHides);
                LocalFreeze(who, Plugin.ImpactFreeze.Value);
                if (Plugin.LogCounters.Value)
                    Plugin.Log.LogInfo($"counter cut on {who.m_name}: {hit.m_damage.GetTotalDamage():0} before resistances ({Vector3.Distance(from, point):0.0} m away)");
            }
        }
    }
}
