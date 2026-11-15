using HarmonyLib;
using UnityEngine;

namespace Iaido_Katana
{
    // the Thunderblood Katana's daisho: its wakizashi is worn at the other hip and drawn in the parry and in the special.
    // this is the owner's side (input, stamina, the hits). IaiStance poses it on every client from the player's network data
    internal static class Daisho
    {
        private static readonly System.Action<Player> ClearQueue =
            AccessTools.MethodDelegate<System.Action<Player>>(AccessTools.Method(typeof(Player), "ClearActionQueue"));

        // the parry's two hits on its timeline (seconds into the move): the katana's, and the wakizashi's that crosses it
        internal const float ParryKatanaAt = 0.11f, ParryWakizashiAt = 0.21f;
        internal const float FirstBeatFreeze = 0.05f; // the katana hit's short hit stop, the wakizashi hit takes the full one
        private const float AimTurn = 120f;           // degrees a second the player follows the camera during the Ranbu
        private const float Push = 4f;
        private const float FrostShare = 0.5f;        // of a wakizashi hit's damage, the rest is slash
        private const float WaveChopShare = 0.25f;    // of a wave's share of the katana's chop (sixteen waves a move)

        private static int? _creatures;
        // the creatures the crescents cut (the Thunderclap's layers)
        internal static int Creatures => _creatures ?? (_creatures = LayerMask.GetMask("character", "character_net", "character_ghost", "hitbox", "character_noenv")).Value;

        private static Player _player;
        private static float _start = -1f;

        internal static bool On(ItemDrop.ItemData weapon) => Plugin.DaishoOn.Value && Katana.IsThunderblood(weapon) && DaishoData.Ranbu != null && DaishoData.Parry != null;

        // the Ranbu is playing for this player: the controls are off
        internal static bool Busy(Player player) => _start >= 0f && player == _player;

        // true when this player's parry plays the daisho's, not the single cut
        internal static bool Parries(Player player) => On(player.GetCurrentWeapon()) && IaiStance.SlashOn && IaiStance.HoldsKatana(player);

        // the game is about to start a heavy attack: with the Thunderblood Katana the Ranbu takes its place
        internal static bool TryStart(Humanoid humanoid, bool secondary)
        {
            if (!secondary || !(humanoid is Player player) || player != Player.m_localPlayer) return false;
            if (!On(player.GetCurrentWeapon())) return false;
            if (Busy(player) || player.InAttack() || player.InDodge() || player.IsSwimming() || !player.IsOnGround() || Thunderclap.Busy(player)) return true;

            // stamina like the game's own attacks: gear and effects first, then less with the Swords skill
            float cost = Plugin.ThunderbloodHeavyStamina.Value * (1f + player.GetEquipmentAttackStaminaModifier());
            player.GetSEMan().ModifyAttackStaminaUsage(cost, ref cost, true);
            cost -= cost * 0.33f * player.GetSkillFactor(Skills.SkillType.Swords);
            if (!player.HaveStamina(cost))
            {
                if (Hud.instance) Hud.instance.StaminaBarEmptyFlash();
                return true;
            }
            player.UseStamina(cost);
            ClearQueue(player);
            _player = player;
            _start = Time.time;
            Face(player, true);
            IaiStance.StartRanbu(player);
            if (Plugin.LogCounters.Value) Plugin.Log.LogInfo($"ranbu: started, {cost:0} stamina");
            return true;
        }

        // every frame for the local player, before the pose
        internal static void Update(Player player)
        {
            if (!Busy(player)) return;
            if (player.IsDead() || !On(player.GetCurrentWeapon()) || player.IsSwimming() || player.IsStaggering())
            {
                Cancel(player, player.IsStaggering() ? "staggered" : "interrupted");
                return;
            }
            Face(player, false);
            if (Time.time - _start >= IaiStance.RanbuSeconds + 0.05f)
            {
                _start = -1f;
                if (Plugin.LogCounters.Value) Plugin.Log.LogInfo("ranbu: finished");
            }
        }

        // the controls while busy: only a dodge gets through, and it ends the Ranbu
        internal static void Controls(Player player, ref Vector3 move, ref bool attack, ref bool attackHold, ref bool secondary,
            ref bool secondaryHold, ref bool block, ref bool blockHold, ref bool jump, ref bool crouch, ref bool run, ref bool autoRun, bool dodge)
        {
            if (!Busy(player)) return;
            if (dodge || (blockHold && jump))
            {
                Cancel(player, "dodged");
                return;
            }
            move = Vector3.zero;
            attack = attackHold = secondary = secondaryHold = block = blockHold = jump = crouch = run = autoRun = false;
        }

        internal static void Cancel(Player player, string why)
        {
            if (Plugin.LogCounters.Value) Plugin.Log.LogInfo($"ranbu: {why}");
            _start = -1f;
            IaiStance.StopRanbu(player);
        }

        // the player turns toward where the camera looks: at once when the move starts, then slowly
        private static void Face(Player player, bool at)
        {
            var look = player.GetLookDir();
            look.y = 0f;
            if (look.sqrMagnitude < 0.01f) return;
            var target = Quaternion.LookRotation(look.normalized);
            player.transform.rotation = at ? target : Quaternion.RotateTowards(player.transform.rotation, target, AimTurn * Time.deltaTime);
        }

        // a wakizashi's blow: half slash, half frost (the frost slows what it does not kill, and most Deep North creatures
        // resist it, so the wakizashi controls more than it damages)
        internal static HitData.DamageTypes Frostify(HitData.DamageTypes damage)
        {
            float slash = damage.m_slash + damage.m_pierce + damage.m_blunt;
            return new HitData.DamageTypes { m_slash = slash * (1f - FrostShare), m_frost = slash * FrostShare };
        }

        internal const string Workbench = "$piece_workbench";

        // a wave crossed a tree, log or stump: its share of the katana's chop, once per cut. inside a workbench's build
        // range nothing is chopped (ChopBaseProtection, like the blasts of AmmoVariants)
        internal static void WaveChop(Crescent.Group group, ItemDrop.ItemData weapon, IDestructible tree, Vector3 point, Vector3 dir)
        {
            if (Plugin.ChopBaseProtection.Value && CraftingStation.HaveBuildStationInRange(Workbench, point) != null)
            {
                if (Plugin.LogCounters.Value && !_protectedLogged) Plugin.Log.LogInfo("ranbu wave: no chop inside a workbench's range");
                _protectedLogged = true;
                return;
            }
            Chop.Hit(group.Owner, weapon, tree, point, dir, group.DamageShare * WaveChopShare);
            if (Plugin.LogCounters.Value)
                Plugin.Log.LogInfo($"ranbu wave chop: {Chop.Damage(weapon) * group.DamageShare * WaveChopShare:0.#} chop, tier {Chop.Tier(weapon)} ({(group.Wakizashi ? "wakizashi" : "katana")})");
        }

        private static bool _protectedLogged;

        // a crescent cut a creature: its share of the katana's damage, once per cut
        internal static void CrescentHit(Crescent.Group group, Character who, Vector3 dir)
        {
            var player = group.Owner;
            var weapon = player ? player.GetCurrentWeapon() : null;
            if (!Katana.IsThunderblood(weapon)) return;
            var hit = new HitData();
            hit.m_damage = group.Wakizashi ? Frostify(weapon.GetDamage()) : weapon.GetDamage();
            hit.m_damage.Modify(group.DamageShare * player.GetRandomSkillFactor(Skills.SkillType.Swords));
            hit.m_point = who.GetCenterPoint();
            hit.m_dir = dir;
            hit.m_attacker = player.GetZDOID();
            hit.m_skill = Skills.SkillType.Swords;
            hit.m_hitType = HitData.HitType.PlayerHit;
            hit.m_toolTier = (short)weapon.m_shared.m_toolTier;
            hit.m_pushForce = Push;
            hit.m_blockable = false;
            hit.m_dodgeable = false;
            who.Damage(hit);
            KatanaFx.HitEffect(weapon.m_shared.m_hitEffect, hit.m_point, Quaternion.LookRotation(dir), 0f);
            if (Plugin.LogCounters.Value)
                Plugin.Log.LogInfo($"ranbu crescent on {who.m_name}: {hit.m_damage.GetTotalDamage():0} before resistances ({(group.Wakizashi ? "wakizashi" : "katana")}, x{group.DamageShare:0.00})");
        }
    }
}
