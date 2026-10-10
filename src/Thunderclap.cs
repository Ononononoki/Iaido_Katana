using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Iaido_Katana
{
    // the flametal katana's heavy attack, after zenitsu's thunderclap and flash in the user's inspiration clip: the player
    // sinks into a low crouch with the blade sheathed while it gathers, then draws and cuts while flashing forward, and
    // every creature on the way takes a full hit. this is the owner's side (input, stamina, the dash, the hits), IaiStance
    // poses it on every client from the player's network data
    internal static class Thunderclap
    {
        private static readonly AccessTools.FieldRef<Character, Rigidbody> BodyOf = AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");
        private static readonly System.Action<Player> ClearQueue =
            AccessTools.MethodDelegate<System.Action<Player>>(AccessTools.Method(typeof(Player), "ClearActionQueue"));

        // the game's layers, looked up on first use (it needs the engine running)
        private static int? _ground, _walls, _creatures;
        private static int Ground => _ground ?? (_ground = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle")).Value;
        private static int Walls => _walls ?? (_walls = LayerMask.GetMask("Default", "static_solid", "piece", "vehicle")).Value;
        private static int Creatures => _creatures ?? (_creatures = LayerMask.GetMask("character", "character_net", "character_ghost", "hitbox", "character_noenv")).Value;

        private const float WallMargin = 0.35f;   // the dash stops this far before a wall
        private const float BodyHeight = 1f;      // the path is swept at this height
        private const float WallRadius = 0.4f;
        private const float MaxStep = 0.6f;       // ground climbing more than this in a tenth of the dash is a wall
        private const float TurnSpeed = 270f;     // degrees a second the crouch turns toward the camera
        private const float Push = 8f;
        private const float GraceAfter = 0.08f;   // unhittable a little past the dash
        private const string TrailFx = "fx_eikthyr_forwardshockwave";
        internal const float DashSeconds = 0.25f; // the dash's time, the cut itself is quicker (Thunder.Cut)
        internal const float TrailEmitters = 4.25f; // eikthyr's lightning starts this far in front of its effect's origin

        private static Player _player;
        private static float _start = -1f;
        private static float _prep;
        private static bool _released;
        private static Vector3 _from, _dir;
        private static float _distance;
        private static float _groundY;
        private static bool _impact;
        private static bool _trailMade;
        private static readonly List<Target> Targets = new List<Target>();

        private sealed class Target
        {
            internal Character Who;
            internal float At; // meters along the path
            internal bool Hit;
        }

        private static float Elapsed => Time.time - _start;

        // the player is gathering, cutting or holding the cut, so the controls are off
        internal static bool Busy(Player player) => _start >= 0f && player == _player;

        // the dash and a moment after it: nothing hits the player, like a dodge
        internal static bool Invincible(Player player) =>
            Busy(player) && _released && Elapsed < _prep + DashSeconds + GraceAfter;

        // the game is about to start a heavy attack: with the flametal katana the thunderclap takes its place
        internal static bool TryStart(Humanoid humanoid, bool secondary)
        {
            if (!secondary || !Plugin.ThunderOn.Value || !(humanoid is Player player) || player != Player.m_localPlayer) return false;
            var weapon = player.GetCurrentWeapon();
            if (!Plugin.IsFlametal(weapon)) return false;
            if (Busy(player) || player.InAttack() || player.InDodge() || player.IsSwimming() || !player.IsOnGround()) return true;

            // stamina like the game's own attacks: gear and effects first, then less with the Swords skill
            float cost = Plugin.ThunderStamina.Value * (1f + player.GetEquipmentAttackStaminaModifier());
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
            _prep = Plugin.ThunderPrep.Value;
            _released = false;
            _impact = false;
            _trailMade = false;
            Targets.Clear();
            IaiStance.StartThunder(player, _prep);
            if (Plugin.LogCounters.Value) Plugin.Log.LogInfo($"thunderclap: gathering for {_prep:0.00} s, {cost:0} stamina");
            return true;
        }

        // every frame for the local player, before the pose
        internal static void Update(Player player)
        {
            if (!Busy(player)) return;
            bool flametal = Plugin.IsFlametal(player.GetCurrentWeapon());
            if (player.IsDead() || !flametal || player.IsSwimming() || (!_released && player.IsStaggering()))
            {
                Cancel(player, player.IsStaggering() ? "staggered" : "interrupted");
                return;
            }
            if (!_released)
            {
                Aim(player);
                if (Elapsed >= _prep) Release(player);
            }
            if (_released) HitPassed(player);
            if (_released && !_trailMade && Elapsed >= _prep + DashSeconds) LightningTrail(player);
            // the blade is back in its scabbard: the controls come back, the pose rises on its own
            if (Elapsed >= Thunder.SheathedAt(_prep)) _start = -1f;
        }

        // the controls while busy: only a dodge gets through, and it ends the thunderclap
        internal static void Controls(Player player, ref Vector3 move, ref bool attack, ref bool attackHold, ref bool secondary,
            ref bool secondaryHold, ref bool block, ref bool blockHold, ref bool jump, ref bool crouch, ref bool run, ref bool autoRun, bool dodge)
        {
            if (!Busy(player)) return;
            if ((dodge || (blockHold && jump)) && !Invincible(player))
            {
                Cancel(player, "dodged");
                return;
            }
            move = Vector3.zero;
            attack = attackHold = secondary = secondaryHold = block = blockHold = jump = crouch = run = autoRun = false;
        }

        private static void Cancel(Player player, string why)
        {
            if (Plugin.LogCounters.Value) Plugin.Log.LogInfo($"thunderclap: {why}{(_released ? " after the cut" : ", stamina lost")}");
            _start = -1f;
            IaiStance.StopThunder(player);
        }

        // while gathering the crouch turns toward where the camera looks, so the cut can be aimed
        private static void Aim(Player player)
        {
            var look = player.GetLookDir();
            look.y = 0f;
            if (look.sqrMagnitude < 0.01f) return;
            var turned = Quaternion.RotateTowards(player.transform.rotation, Quaternion.LookRotation(look.normalized), TurnSpeed * Time.deltaTime);
            player.transform.rotation = turned;
            BodyOf(player).rotation = turned;
        }

        // the cut: where the dash can go, who is on the way, and eikthyr's lightning along it
        private static void Release(Player player)
        {
            _released = true;
            _from = player.transform.position;
            _dir = player.transform.forward;
            _dir.y = 0f;
            _dir = _dir.sqrMagnitude > 0.01f ? _dir.normalized : Vector3.forward;
            _groundY = _from.y;
            _distance = Reach(_from, _dir, Plugin.ThunderDistance.Value);

            Targets.Clear();
            var seen = new HashSet<Character>();
            foreach (var hit in Physics.SphereCastAll(_from + Vector3.up * BodyHeight, Plugin.ThunderWidth.Value, _dir, _distance, Creatures))
            {
                var who = hit.collider ? hit.collider.GetComponentInParent<Character>() : null;
                if (!who || who == player || who.IsDead() || !seen.Add(who) || !Enemy(player, who)) continue;
                Targets.Add(new Target { Who = who, At = Mathf.Clamp(Vector3.Dot(who.transform.position - _from, _dir), 0f, _distance) });
            }
            Targets.Sort((a, b) => a.At.CompareTo(b.At));
            if (Plugin.LogCounters.Value) Plugin.Log.LogInfo($"thunderclap: cut, dash {_distance:0.0} m, {Targets.Count} on the way");
        }

        // the dash is over: eikthyr's lightning from where the player stands back along the path. a network effect, so
        // only this client makes it and everyone sees it. its lightning starts about 4 m in front of the effect and runs
        // on ahead, so the effect stands that far ahead of the player, facing back, and the bolts leave right from them
        private static void LightningTrail(Player player)
        {
            _trailMade = true;
            if (!Plugin.ThunderTrailOn.Value || !ZNetScene.instance) return;
            var prefab = ZNetScene.instance.GetPrefab(TrailFx);
            if (prefab) Object.Instantiate(prefab, player.transform.position + _dir * TrailEmitters, Quaternion.LookRotation(-_dir));
            else Plugin.Log.LogWarning($"thunderclap: effect '{TrailFx}' not found");
        }

        // how far the dash can go: short of walls, rocks and buildings, and of ground that climbs like a wall
        internal static float Reach(Vector3 from, Vector3 dir, float max)
        {
            float reach = max;
            if (Physics.SphereCast(from + Vector3.up * BodyHeight, WallRadius, dir, out var wall, max, Walls))
                reach = Mathf.Max(0f, wall.distance - WallMargin);
            float y = from.y;
            const int steps = 10;
            for (int i = 1; i <= steps; i++)
            {
                float d = reach * i / steps;
                if (!GroundAt(from + dir * d, y, out float ground)) continue; // over a drop the dash goes on, the fall comes after
                if (ground - y > MaxStep)
                {
                    reach = reach * (i - 1) / steps;
                    break;
                }
                y = ground;
            }
            return reach;
        }

        private static bool GroundAt(Vector3 point, float lastY, out float y)
        {
            y = lastY;
            if (!Physics.Raycast(new Vector3(point.x, lastY + 2f, point.z), Vector3.down, out var hit, 4f, Ground)) return false;
            y = hit.point.y;
            return true;
        }

        // fast out of the crouch, settling at the end
        private static float Travelled(float elapsedInCut)
        {
            float k = Mathf.Clamp01(elapsedInCut / DashSeconds);
            return _distance * (1f - (1f - k) * (1f - k));
        }

        // every physics step for the local player during the dash: the body moves along the path, over the ground
        internal static void FixedStep(Character character)
        {
            if (character != _player || !_released || _start < 0f) return;
            float inCut = Elapsed - _prep;
            if (inCut > DashSeconds + Time.fixedDeltaTime) return;
            var point = _from + _dir * Travelled(inCut);
            if (GroundAt(point, _groundY, out float ground)) _groundY = ground;
            point.y = _groundY;
            var body = BodyOf(character);
            body.position = point;
            body.linearVelocity = Vector3.zero;
        }

        // each creature is cut as the player passes it, each in full
        private static void HitPassed(Player player)
        {
            float passed = Travelled(Elapsed - _prep) + 0.5f;
            var weapon = player.GetCurrentWeapon();
            foreach (var target in Targets)
            {
                if (target.Hit || target.At > passed) continue;
                target.Hit = true;
                var who = target.Who;
                if (!who || who.IsDead()) continue;

                var hit = new HitData();
                hit.m_damage = weapon.GetDamage();
                hit.m_damage.Modify(Plugin.ThunderDamage.Value * player.GetRandomSkillFactor(Skills.SkillType.Swords));
                hit.m_point = who.GetCenterPoint();
                hit.m_dir = _dir;
                hit.m_attacker = player.GetZDOID();
                hit.m_skill = Skills.SkillType.Swords;
                hit.m_hitType = HitData.HitType.PlayerHit;
                hit.m_toolTier = (short)weapon.m_shared.m_toolTier;
                hit.m_pushForce = Push;
                hit.m_blockable = false;
                hit.m_dodgeable = false;
                who.Damage(hit);

                KatanaFx.HitEffect(weapon.m_shared.m_hitEffect, hit.m_point, Quaternion.LookRotation(_dir), _impact ? 0f : ImpactFrame.InkHides);
                Counter.LocalFreeze(who, Plugin.ImpactFreeze.Value);
                if (!_impact)
                {
                    _impact = true;
                    ImpactFrame.Play(hit.m_point);
                }
                player.RaiseSkill(Skills.SkillType.Swords, 1f);
                if (Plugin.LogCounters.Value)
                    Plugin.Log.LogInfo($"thunderclap on {who.m_name}: {hit.m_damage.GetTotalDamage():0} before resistances, {target.At:0.0} m along");
            }
        }

        // who the cut may hit: the same rule as the game's melee, and players only with pvp on both sides
        private static bool Enemy(Player player, Character who)
        {
            if (who.IsPlayer()) return Plugin.CounterInPvP.Value && player.IsPVPEnabled() && who.IsPVPEnabled();
            var ai = who.GetBaseAI();
            return BaseAI.IsEnemy(player, who) || (ai && ai.IsAggravatable());
        }
    }
}
