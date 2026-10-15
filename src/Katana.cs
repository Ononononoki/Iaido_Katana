using System;
using HarmonyLib;
using UnityEngine;

namespace Iaido_Katana
{
    // the steel katana's stats and recipe, and the pure math behind the parry, counter and swing timing
    internal static class Katana
    {
        internal const string SteelPrefab = "IaidoKatanaSteel";
        internal const string SteelToken = "$item_iaidokatana_steel";
        internal const string FlametalPrefab = "IaidoKatanaFlametal";
        internal const string FlametalToken = "$item_iaidokatana_flametal";
        internal const float VanillaParryWindow = 0.25f;

        // item, amount to craft, extra amount per upgrade level (the game charges per level × (quality - 1), 4× at level 4).
        // four materials each, so the crafting panel shows them all at once: iron and coal (the steel), fine wood (the
        // scabbard), linen thread (the handle's wrap)
        internal static readonly (string item, int amount, int perLevel)[] SteelRecipe =
        {
            ("Iron", 50, 10),
            ("Coal", 100, 20),
            ("FineWood", 20, 0),
            ("LinenThread", 10, 3)
        };

        // flametal (the blade), ashwood (the scabbard), asksvin hide (the wrap), one iolite (the lightning, like the game's
        // lightning weapons)
        internal static readonly (string item, int amount, int perLevel)[] FlametalRecipe =
        {
            ("FlametalNew", 50, 10),
            ("Blackwood", 20, 0),
            ("AskHide", 5, 2),
            ("GemstoneBlue", 1, 0)
        };

        // the battle idol the Forge of Potential takes to refine a katana past level 4, the tier of the game's swords of
        // its biome. it is hidden while crafting, and the forge skips a recipe without one
        internal const string SteelIdol = "Upgrader4Weapon";     // black metal
        internal const string FlametalIdol = "Upgrader6Weapon";  // flametal

        private static readonly Func<Humanoid, ItemDrop.ItemData> GetCurrentBlocker =
            AccessTools.MethodDelegate<Func<Humanoid, ItemDrop.ItemData>>(AccessTools.Method(typeof(Humanoid), "GetCurrentBlocker"));
        private static readonly AccessTools.FieldRef<Humanoid, float> BlockTimer = AccessTools.FieldRefAccess<Humanoid, float>("m_blockTimer");

        internal static bool IsKatana(ItemDrop.ItemData item) =>
            item?.m_shared != null && (item.m_shared.m_name == SteelToken || item.m_shared.m_name == FlametalToken);

        // 40% of a black metal sword, a guard that barely blocks, a parry that stops any normal plains hit, little knockback
        internal static void ApplySteelStats(ItemDrop.ItemData.SharedData shared)
        {
            shared.m_damages = new HitData.DamageTypes { m_slash = 38f };
            shared.m_damagesPerLevel = new HitData.DamageTypes { m_slash = 2.4f };
            shared.m_blockPower = 10f;
            shared.m_blockPowerPerLevel = 1f;
            shared.m_timedBlockBonus = 13f;
            shared.m_deflectionForce = 5f;
            shared.m_deflectionForcePerLevel = 0f;
            shared.m_maxQuality = 4;
        }

        // 60 slash plus lightning worth a quarter of it (the user's call). block 13 so a parry stops a normal ashlands hit
        internal static void ApplyFlametalStats(ItemDrop.ItemData.SharedData shared)
        {
            shared.m_damages = new HitData.DamageTypes { m_slash = 60f, m_lightning = 15f };
            shared.m_damagesPerLevel = new HitData.DamageTypes { m_slash = 2.4f, m_lightning = 0.6f };
            shared.m_blockPower = 13f;
            shared.m_blockPowerPerLevel = 1f;
            shared.m_timedBlockBonus = 13f;
            shared.m_deflectionForce = 5f;
            shared.m_deflectionForcePerLevel = 0f;
            shared.m_maxQuality = 4;
        }

        // called from the patched BlockAttack in place of the game's 0.25 s. it runs on the blocking player's own client,
        // so the player's own mode applies
        public static float ParryWindow(Humanoid humanoid) =>
            humanoid is Player && IsKatana(GetCurrentBlocker(humanoid))
                ? KatanaWindow(Plugin.ParryPrecise.Value, Plugin.ParryNormalMs.Value, Plugin.ParryWindowMs.Value) : VanillaParryWindow;

        // the normal katana window (200 ms), the tighter one in precise mode (150 ms)
        internal static float KatanaWindow(bool precise, int normalMs, int preciseMs) => Window(precise ? preciseMs : normalMs);

        internal static float Window(int milliseconds) => Mathf.Clamp(milliseconds, 50, 250) / 1000f;

        // what precise mode adds to a counter
        internal static float PreciseFactor(bool precise, float bonus) => precise ? bonus : 1f;

        // each katana's base sword's block power (Krom 52, the Slayer 64) by the katana's name, kept when the katanas are
        // made. by name: an item in the inventory does not carry the item data object the katana was made with (12 Oct,
        // the in-game log showed the lookup by that object failing)
        internal static readonly System.Collections.Generic.Dictionary<string, float> BaseBlockPower =
            new System.Collections.Generic.Dictionary<string, float>();

        // a block wears the blocker by the blocked damage over its block power, and the katanas barely block (10, 13), so
        // a block wore them about five times faster than their base swords. a katana's wear from a block is scaled as if
        // it blocked like its base sword (a perfect parry is left alone, its 13x bonus already wears it less)
        internal static float BlockWearScale(float katanaPower, float basePower) =>
            basePower > katanaPower && katanaPower > 0f ? katanaPower / basePower : 1f;

        internal static ItemDrop.ItemData CurrentBlocker(Humanoid humanoid) => GetCurrentBlocker(humanoid);

        // the game's own parry test from BlockAttack, true only while a katana blocks
        internal static bool IsKatanaParry(Humanoid humanoid)
        {
            var blocker = GetCurrentBlocker(humanoid);
            if (!IsKatana(blocker) || blocker.m_shared.m_timedBlockBonus <= 1f) return false;
            float timer = BlockTimer(humanoid);
            return timer != -1f && timer < ParryWindow(humanoid);
        }

        // counter damage as a multiple of the katana's damage. block skill (0 to 1) raises it from 1x up to blockBonusMax
        internal static float CounterFactor(float swordsFactor, float blockSkill, bool ranged, float multiplier, float blockBonusMax, float reflect) =>
            multiplier * swordsFactor * (1f + (blockBonusMax - 1f) * Mathf.Clamp01(blockSkill)) * (ranged ? reflect : 1f);

        // whether a creature is inside a swing: to is from the player's middle to the creature's, ahead the swing's
        // flat direction. within range of its body's edge and within half the angle either side, like the game's melee,
        // and within height up or down (the range when not given)
        internal static bool InSwing(Vector3 to, Vector3 ahead, float radius, float range, float angle, float height = -1f)
        {
            var flat = new Vector3(to.x, 0f, to.z);
            if (flat.magnitude - radius > range || Mathf.Abs(to.y) > (height < 0f ? range : height)) return false;
            return flat.sqrMagnitude < 1e-4f || Vector3.Angle(ahead, flat) <= angle * 0.5f;
        }

        // animation speed at a point of a re-timed swing before its hit. the windup and the strike both scale with
        // pace, so the time to the hit is hitTime / pace
        internal static float WarpSpeed(float animTime, float hitTime, float windupShare, float windupSpeed, float strikeSpeed, float pace) =>
            (animTime < hitTime * windupShare ? windupSpeed : strikeSpeed) * pace;

        // raise speed that makes the whole swing last `total` seconds: start delay + raise + slash + recovery after the hit.
        // -1 when the time left for the raise would make it faster than the slash
        internal static float RaiseSpeedForTotal(float hitTime, float windupShare, float slashSpeed, float entry, float afterHit, float total)
        {
            if (slashSpeed <= 0f || hitTime <= 0f) return -1f;
            float raiseAnim = windupShare * hitTime;
            float raiseReal = total - entry - (1f - windupShare) * hitTime / slashSpeed - afterHit;
            return raiseReal <= raiseAnim / slashSpeed ? -1f : raiseAnim / raiseReal;
        }

        // windup speed that keeps the time to the hit unchanged for a given strike speed, -1 when it cannot add up
        internal static float WindupFor(float windupShare, float strikeSpeed)
        {
            if (windupShare <= 0f || windupShare >= 1f || strikeSpeed <= 0f) return -1f;
            float rest = 1f - (1f - windupShare) / strikeSpeed;
            return rest < 0.05f ? -1f : windupShare / rest;
        }

        // speed after a slow windup that keeps the time to the hit unchanged, -1 when the settings cannot add up
        internal static float StrikeSpeed(float windupShare, float windupSpeed)
        {
            if (windupShare <= 0f || windupShare >= 1f || windupSpeed <= 0f) return -1f;
            float rest = 1f - windupShare / windupSpeed;
            return rest < 0.05f ? -1f : (1f - windupShare) / rest;
        }
    }
}
