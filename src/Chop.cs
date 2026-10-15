using System.Collections.Generic;
using UnityEngine;

namespace Iaido_Katana
{
    // the counter's cut and the thunderclap's dash chop trees, logs and stumps like an axe: a chop-only hit with the
    // katana's chop and tool tier (Chop settings), so a tree that needs a better axe still takes nothing
    internal static class Chop
    {
        // the layers trees, logs and stumps sit on, looked up on first use (it needs the engine running)
        private static int? _mask;
        internal static int Mask => _mask ?? (_mask = LayerMask.GetMask("Default", "static_solid", "Default_small")).Value;

        internal static bool On(ItemDrop.ItemData weapon) => Plugin.ChopOn.Value && Katana.IsKatana(weapon);

        internal static float Damage(ItemDrop.ItemData weapon) => Plugin.IsFlametal(weapon) ? Plugin.FlametalChop.Value : Plugin.SteelChop.Value;

        internal static int Tier(ItemDrop.ItemData weapon) => Plugin.IsFlametal(weapon) ? Plugin.FlametalChopTier.Value : Plugin.SteelChopTier.Value;

        // the tree, log or stump a collider belongs to, or null
        internal static IDestructible Tree(Collider collider)
        {
            var found = collider ? collider.GetComponentInParent<IDestructible>() : null;
            return found is Component part && part && found.GetDestructibleType() == DestructibleType.Tree ? found : null;
        }

        // one chop on a tree, log or stump
        internal static void Hit(Player player, ItemDrop.ItemData weapon, IDestructible tree, Vector3 point, Vector3 dir)
        {
            var hit = new HitData();
            hit.m_damage.m_chop = Damage(weapon);
            hit.m_toolTier = (short)Tier(weapon);
            hit.m_point = point;
            hit.m_dir = dir;
            hit.m_attacker = player.GetZDOID();
            hit.m_skill = Skills.SkillType.WoodCutting;
            hit.m_hitType = HitData.HitType.PlayerHit;
            tree.Damage(hit);
        }

        // every tree, log and stump with a part inside the cut's fan, once each, not through walls or rocks
        internal static int CutAround(Player player, ItemDrop.ItemData weapon, Vector3 from, Vector3 ahead, float range, float angle, float height)
        {
            var done = new HashSet<IDestructible>();
            foreach (var collider in Physics.OverlapSphere(from, Mathf.Max(range, height) + 1f, Mask))
            {
                var tree = Tree(collider);
                if (tree == null || done.Contains(tree)) continue;
                var point = Closest(collider, from);
                var to = point - from;
                bool inFront = new Vector3(to.x, 0f, to.z).sqrMagnitude < Near * Near && Vector3.Dot(new Vector3(to.x, 0f, to.z), ahead) > -0.1f && Mathf.Abs(to.y) <= height;
                if (!(inFront || Katana.InSwing(to, ahead, 0f, range, angle, height)) || Blocked(from, point, tree)) continue;
                done.Add(tree);
                var dir = point - from;
                Hit(player, weapon, tree, point, dir.sqrMagnitude > 1e-4f ? dir.normalized : ahead);
            }
            return done.Count;
        }

        private const float Near = 1f; // anything this close in front is in reach, whatever the angle

        // the nearest point of the collider's own shape (a long log lying crosswise has a box of bounds far bigger than
        // itself), its bounds where the shape can not answer
        private static Vector3 Closest(Collider collider, Vector3 from)
        {
            bool exact = collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider || collider is MeshCollider mesh && mesh.convex;
            return exact ? collider.ClosestPoint(from) : collider.ClosestPointOnBounds(from);
        }

        // something other than the tree itself stands between
        private static bool Blocked(Vector3 from, Vector3 point, IDestructible tree) =>
            Physics.Linecast(from, point, out var block, Thunderclap.Walls) && Tree(block.collider) != tree;
    }
}
