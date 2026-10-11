using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace Iaido_Katana
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInDependency("com.jotunn.jotunn", BepInDependency.DependencyFlags.HardDependency)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "onononoki.Iaido_Katana";
        public const string ModName = "Iaido_Katana";
        public const string ModVersion = "0.1.2";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> CounterEnabled;
        internal static ConfigEntry<float> CounterMultiplier;
        internal static ConfigEntry<float> BlockBonusMax;
        internal static ConfigEntry<float> ReflectMultiplier;
        internal static ConfigEntry<float> CounterDelay;
        internal static ConfigEntry<bool> CounterInPvP;
        internal static ConfigEntry<int> ParryWindowMs;
        internal static ConfigEntry<int> ParryNormalMs;
        internal static ConfigEntry<bool> ParryPrecise;
        internal static ConfigEntry<float> ParryPreciseBonus;
        internal static ConfigEntry<bool> SwingTimeWarp;
        internal static ConfigEntry<float> WindupShare;
        internal static ConfigEntry<float> WindupSpeed;
        internal static ConfigEntry<float> HeavySpeed;
        internal static ConfigEntry<float> NormalStrikeBoost;
        internal static ConfigEntry<float> HeavyStrikeBoost;
        internal static ConfigEntry<float> HeavyDuration;
        internal static ConfigEntry<float> HeavyDamageMultiplier;
        internal static ConfigEntry<float> HeavyStamina;
        internal static ConfigEntry<float> FlametalSwingStamina;
        internal static ConfigEntry<float> FlametalHeavyStamina;

        internal static ConfigEntry<float> NormalRange;
        internal static ConfigEntry<float> NormalAngle;
        internal static ConfigEntry<float> NormalRayWidth;
        internal static ConfigEntry<float> HeavyRange;
        internal static ConfigEntry<float> HeavyAngle;
        internal static ConfigEntry<float> HeavyRayWidth;
        internal static ConfigEntry<bool> DumpAnimationClips;

        private static readonly List<ItemDrop.ItemData.SharedData> KatanaShared = new List<ItemDrop.ItemData.SharedData>();
        // the flametal katana's item data, marked when it is made (its name token is only set later by jotunn)
        private static readonly HashSet<ItemDrop.ItemData.SharedData> FlametalShared = new HashSet<ItemDrop.ItemData.SharedData>();

        // by its name, like Katana.IsKatana: in the game an item in the inventory can carry a copy of its prefab's data
        internal static bool IsFlametal(ItemDrop.ItemData item) => item?.m_shared != null && item.m_shared.m_name == Katana.FlametalToken;
        internal static ConfigEntry<string> HeavyAnimationFrom;
        internal static ConfigEntry<bool> UpperBodySwings;
        internal static ConfigEntry<bool> OneHandedMoves;
        internal const string UpperSwing = "shovel"; // the game's upper body swing chain (the snow shovel's): shovel0, 1, 2
        internal static ConfigEntry<string> HeavyAnimationSlot;
        internal static ConfigEntry<bool> CounterAnimation;
        internal static ConfigEntry<float> CounterAnimationSpeed;
        internal static ConfigEntry<bool> LogCounters;
        internal static ConfigEntry<bool> ImpactEnabled;
        internal static ConfigEntry<float> ImpactFlashSeconds;
        internal static ConfigEntry<float> ImpactContrast;
        internal static ConfigEntry<float> ImpactShake;
        internal static ConfigEntry<string> ImpactSound;
        internal static ConfigEntry<float> ImpactVolume;
        internal static ConfigEntry<float> ImpactWhite;
        internal static ConfigEntry<bool> ImpactInk;
        internal static ConfigEntry<float> ImpactInkSeconds;
        internal static ConfigEntry<bool> ImpactInkInvert;
        internal static ConfigEntry<bool> ImpactInkCut;
        internal static ConfigEntry<float> ImpactFreeze;
        internal static ConfigEntry<string> ImpactCrash;
        internal static ConfigEntry<float> ImpactCrashPitch;
        internal static ConfigEntry<float> ImpactCrashSeconds;
        internal static ConfigEntry<float> ImpactCrashVolume;
        internal static ConfigEntry<bool> ThunderOn;
        internal static ConfigEntry<float> ThunderDamage;
        internal static ConfigEntry<float> ThunderPrep;
        internal static ConfigEntry<float> ThunderDistance;
        internal static ConfigEntry<float> ThunderWidth;
        internal static ConfigEntry<float> ThunderStamina;
        internal static ConfigEntry<bool> ThunderTrailOn;
        internal static ConfigEntry<string> ImpactClick;
        internal static ConfigEntry<float> ImpactClickPitch;
        internal static ConfigEntry<float> ImpactClickVolume;
        internal static ConfigEntry<bool> KatanaModelOn;
        internal static ConfigEntry<bool> ScabbardOn;
        internal static ConfigEntry<bool> HolsterOn;
        internal static ConfigEntry<bool> SlashTrailOn;
        internal static ConfigEntry<bool> GlintOn;
        internal static ConfigEntry<bool> SparksOn;
        internal static ConfigEntry<bool> StanceEnabled;
        internal static ConfigEntry<float> StanceBlendIn;
        internal static ConfigEntry<float> StanceBlendOut;
        internal static ConfigEntry<float> StanceGripSide;
        internal static ConfigEntry<float> StanceGripHeight;
        internal static ConfigEntry<float> StanceGripForward;
        internal static ConfigEntry<float> StanceBladeYaw;
        internal static ConfigEntry<float> StanceBladePitch;
        internal static ConfigEntry<float> StanceTwist;
        internal static ConfigEntry<float> StanceLean;
        internal static ConfigEntry<float> StanceLeftHand;
        internal static ConfigEntry<float> StanceSpineHold;
        internal static ConfigEntry<bool> StanceSlash;
        internal static ConfigEntry<bool> StanceReady;
        internal static ConfigEntry<bool> StanceReadyRunning;
        internal static ConfigEntry<float> StanceSheathe;
        internal static ConfigEntry<float> StanceZanshin;
        internal static ConfigEntry<float> StanceNoto;

        internal static Plugin Instance;

        // placeholder look until the real model: krom in a darker steel
        private static readonly Color SteelTint = new Color(0.62f, 0.64f, 0.72f);
        // placeholder look of the ashlands katana: slayer with a cold blue cast for the lightning
        private static readonly Color FlametalTint = new Color(0.7f, 0.78f, 1f);

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            var sync = new ConfigurationManagerAttributes { IsAdminOnly = true };

            CounterEnabled = Config.Bind("Counter", "Enabled", true,
                new ConfigDescription("A perfect parry with a katana strikes the attacker back on its own.", null, sync));
            CounterMultiplier = Config.Bind("Counter", "DamageMultiplier", 2.5f,
                new ConfigDescription("Counter damage as a multiple of the katana's damage, before Block skill. 2.5 makes it hit like a Black Metal sword.",
                    new AcceptableValueRange<float>(0.5f, 10f), sync));
            BlockBonusMax = Config.Bind("Counter", "BlockSkillBonus", 1.5f,
                new ConfigDescription("Extra counter damage from Block skill: 1x at skill 0, rising to this at skill 100.",
                    new AcceptableValueRange<float>(1f, 3f), sync));
            ReflectMultiplier = Config.Bind("Counter", "ReflectMultiplier", 0.5f,
                new ConfigDescription("Share of the counter that a parried arrow, spear or spell sends to the shooter.",
                    new AcceptableValueRange<float>(0f, 1f), sync));
            CounterDelay = Config.Bind("Counter", "Delay", 0.1f,
                new ConfigDescription("Seconds between the parry and the counter hit. The game only doubles damage on a staggered target once its stagger has started, so keep it at 0.1 or more.",
                    new AcceptableValueRange<float>(0f, 0.5f), sync));
            CounterInPvP = Config.Bind("Counter", "PvP", false,
                new ConfigDescription("Counters also hit other players.", null, sync));
            // each player picks the mode for themselves, the server sets what it means
            ParryPrecise = Config.Bind("Parry", "PreciseMode", false,
                "Off: katanas parry with the normal window (NormalWindowMs). On: a tighter window (WindowMs), and every counter hits harder (PreciseCounterMultiplier). Your own choice, also in multiplayer.");
            ParryNormalMs = Config.Bind("Parry", "NormalWindowMs", 200,
                new ConfigDescription("Parry window of katanas in milliseconds, out of precise mode. Every other weapon keeps the game's 250.",
                    new AcceptableValueRange<int>(50, 250), sync));
            ParryWindowMs = Config.Bind("Parry", "WindowMs", 150,
                new ConfigDescription("Parry window of katanas in precise mode, in milliseconds.",
                    new AcceptableValueRange<int>(50, 250), sync));
            ParryPreciseBonus = Config.Bind("Parry", "PreciseCounterMultiplier", 1.15f,
                new ConfigDescription("Counters and reflected hits in precise mode are this much stronger. Swings, the heavy attack and the thunderclap do not change.",
                    new AcceptableValueRange<float>(1f, 2f), sync));
            SwingTimeWarp = Config.Bind("Swing", "TimeWarp", true,
                "Times katana swings so their hit lands when the greatsword's did. With Swing.UpperBody an even pace up to the hit, without it a slow windup and a fast strike. Only changes how your own swings look.");
            WindupShare = Config.Bind("Swing", "WindupShare", 0.5f,
                new ConfigDescription("Only with Swing.UpperBody off. Share of the animation before the hit that plays as the slow windup. Must stay below WindupSpeed.", new AcceptableValueRange<float>(0.1f, 0.9f)));
            WindupSpeed = Config.Bind("Swing", "WindupSpeed", 0.6f,
                new ConfigDescription("Only with Swing.UpperBody off. Animation speed during the windup. The strike speeds up to make up the time (3x with the defaults).", new AcceptableValueRange<float>(0.3f, 1f)));
            NormalStrikeBoost = Config.Bind("Swing", "NormalStrikeBoost", 1.875f,
                new ConfigDescription("Only with Swing.UpperBody off. Makes the strike of normal swings this much faster. The windup slows down so the hit lands at the same moment as vanilla.", new AcceptableValueRange<float>(1f, 3f)));
            HeavyStrikeBoost = Config.Bind("Swing", "HeavyStrikeBoost", 1.3f,
                new ConfigDescription("Only for a borrowed heavy motion (HeavyAnimationFrom other than UpperBody). Makes the slash of the heavy attack this much faster. Its raise slows down so the hit lands at the same moment.", new AcceptableValueRange<float>(1f, 3f)));
            HeavyDuration = Config.Bind("Swing", "HeavyDuration", 2f,
                new ConfigDescription("Total length of the heavy attack in seconds, recovery included. Only the raise changes to fit, the slash keeps its speed. 0 turns this off.", new AcceptableValueRange<float>(0f, 4f)));
            HeavyDamageMultiplier = Config.Bind("Counter", "HeavyDamageMultiplier", 6f,
                new ConfigDescription("Damage of the heavy attack as a multiple of the katana's damage (Krom's thrust is 3).", new AcceptableValueRange<float>(1f, 10f), sync));
            HeavyDamageMultiplier.SettingChanged += (s, e) => ApplyAttackSettings();
            HeavyStamina = Config.Bind("Counter", "HeavyStamina", 50f,
                new ConfigDescription("Stamina cost of the heavy attack (Krom's thrust is 40), so it cannot be spammed.", new AcceptableValueRange<float>(0f, 100f), sync));
            HeavyStamina.SettingChanged += (s, e) => ApplyAttackSettings();
            // the flametal katana hits much harder, so its swings cost more (20% over the steel one)
            FlametalSwingStamina = Config.Bind("Counter", "FlametalSwingStamina", 24f,
                new ConfigDescription("Stamina cost of the Flametal Katana's normal swings (the Steel Katana's are 20, like Krom's).", new AcceptableValueRange<float>(0f, 100f), sync));
            FlametalSwingStamina.SettingChanged += (s, e) => ApplyAttackSettings();
            FlametalHeavyStamina = Config.Bind("Counter", "FlametalHeavyStamina", 60f,
                new ConfigDescription("Stamina cost of the Flametal Katana's heavy attack (HeavyStamina is the Steel Katana's).", new AcceptableValueRange<float>(0f, 100f), sync));
            FlametalHeavyStamina.SettingChanged += (s, e) => ApplyAttackSettings();
            // which swings the katanas play is set by the server, so every player sees the same, and applied when it arrives.
            // the katana's own swings and the one-handed movement are experimental, off by default (12 Oct: they felt
            // weightless and cut off from the body)
            UpperBodySwings = Config.Bind("Swing", "UpperBody", false,
                new ConfigDescription("Experimental, work in progress. Katana swings play on the game's upper body animation layer, so the legs keep walking during a swing, and the katana's own cuts show over it. Off: the greatsword swings. Set by the server in multiplayer.", null, sync));
            OneHandedMoves = Config.Bind("Swing", "OneHandedMoves", false,
                new ConfigDescription("Experimental, work in progress. The katana uses the one-handed sword's idle, walk, run and block animations (it stays a two-handed weapon). Set by the server in multiplayer.", null, sync));
            HeavyAnimationFrom = Config.Bind("Swing", "HeavyAnimationFrom", "SledgeStagbreaker",
                new ConfigDescription("Weapon whose heavy motion the heavy attack borrows, by prefab name (for example SledgeStagbreaker, SledgeIron), with its own legs. The hit stays the katana's own slash. UpperBody (experimental, with Swing.UpperBody on): the katana's own overhead cut. Set by the server in multiplayer.", null, sync));
            HeavyAnimationSlot = Config.Bind("Swing", "HeavyAnimationSlot", "Secondary",
                new ConfigDescription("Only for a borrowed heavy motion. Which of that weapon's attacks to borrow. On the two-handed hammers the secondary is the overhead ground slam. Set by the server in multiplayer.",
                    new AcceptableValueList<string>("Secondary", "Primary"), sync));
            UpperBodySwings.SettingChanged += (s, e) => ApplyMoveSettings();
            OneHandedMoves.SettingChanged += (s, e) => ApplyMoveSettings();
            HeavyAnimationFrom.SettingChanged += (s, e) => ApplyMoveSettings();
            HeavyAnimationSlot.SettingChanged += (s, e) => ApplyMoveSettings();
            HeavySpeed = Config.Bind("Swing", "HeavySpeed", 1.7f,
                new ConfigDescription("Only for a borrowed heavy motion. Speeds up the heavy (secondary) attack up to its hit. The recovery after the hit plays at normal speed.", new AcceptableValueRange<float>(1f, 3f)));
            CounterAnimation = Config.Bind("Swing", "CounterAnimation", true,
                "Play a fast swing on a parry, and land the counter on its hit moment. A placeholder until the iai animation exists.");
            CounterAnimationSpeed = Config.Bind("Swing", "CounterAnimationSpeed", 5.5f,
                new ConfigDescription("Speed of that counter swing.", new AcceptableValueRange<float>(1f, 8f)));
            // hit area of the swings. krom: normal 2.6 m, 90 degrees, 0.5 ray. thrust 3 m, 30 degrees, 0.5 ray
            NormalRange = Config.Bind("Hitbox", "NormalRange", 3.0f,
                new ConfigDescription("Reach of normal swings in meters (Krom 2.6).", new AcceptableValueRange<float>(1.5f, 6f), sync));
            NormalAngle = Config.Bind("Hitbox", "NormalAngle", 110f,
                new ConfigDescription("Width of normal swings in degrees (Krom 90).", new AcceptableValueRange<float>(10f, 180f), sync));
            NormalRayWidth = Config.Bind("Hitbox", "NormalRayWidth", 0.7f,
                new ConfigDescription("Thickness of normal swings in meters (Krom 0.5).", new AcceptableValueRange<float>(0.1f, 2f), sync));
            HeavyRange = Config.Bind("Hitbox", "HeavyRange", 4.0f,
                new ConfigDescription("Reach of the heavy attack in meters (Krom's thrust 3).", new AcceptableValueRange<float>(1.5f, 8f), sync));
            HeavyAngle = Config.Bind("Hitbox", "HeavyAngle", 45f,
                new ConfigDescription("Width of the heavy attack in degrees (Krom's thrust 30).", new AcceptableValueRange<float>(5f, 180f), sync));
            HeavyRayWidth = Config.Bind("Hitbox", "HeavyRayWidth", 0.9f,
                new ConfigDescription("Thickness of the heavy attack in meters (Krom's thrust 0.5).", new AcceptableValueRange<float>(0.1f, 2f), sync));
            foreach (var entry in new[] { NormalRange, NormalAngle, NormalRayWidth, HeavyRange, HeavyAngle, HeavyRayWidth })
                entry.SettingChanged += (s, e) => ApplyAttackSettings();
            // the impact frame is only on your own screen, so every player sets it for themselves
            ImpactEnabled = Config.Bind("Impact", "Enabled", true,
                "Impact frame when a counter lands: a short black and white flash, a camera jolt and a sharp sound. Only on your own screen.");
            ImpactFlashSeconds = Config.Bind("Impact", "FlashSeconds", 0.07f,
                new ConfigDescription("How long the screen stays black and white and white-flashed, in real seconds (0.07 is about 4 frames at 60 fps). 0 turns the flash off.",
                    new AcceptableValueRange<float>(0f, 0.3f)));
            ImpactContrast = Config.Bind("Impact", "FlashContrast", 1.5f,
                new ConfigDescription("Contrast during the flash, as a multiple of the game's own.", new AcceptableValueRange<float>(1f, 1.65f)));
            ImpactWhite = Config.Bind("Impact", "WhiteFlash", 0.45f,
                new ConfigDescription("Strength of a white flash over the whole screen, held for FlashSeconds and then faded out. With the ink frame it comes right after it. 0 turns it off.", new AcceptableValueRange<float>(0f, 1f)));
            ImpactInk = Config.Bind("Impact", "InkFrame", true,
                "Ink frame: the moment of the cut freezes as a stark black and white picture, like an anime impact frame. Only on your own screen.");
            ImpactInkSeconds = Config.Bind("Impact", "InkSeconds", 0.075f,
                new ConfigDescription("How long the ink frame stays, in real seconds.", new AcceptableValueRange<float>(0f, 0.5f)));
            ImpactInkInvert = Config.Bind("Impact", "InkInvert", false,
                "Swap black and white in the ink frame (bright things turn black).");
            ImpactInkCut = Config.Bind("Impact", "InkCut", true,
                "Draw the cut as a sharp streak through the enemy in the ink frame.");
            ImpactFreeze = Config.Bind("Impact", "FreezeSeconds", 0.15f,
                new ConfigDescription("How long both fighters freeze when a counter lands, the game's own hit stop (a normal hit is much shorter).", new AcceptableValueRange<float>(0f, 0.4f)));
            ImpactShake = Config.Bind("Impact", "Shake", 1.7f,
                new ConfigDescription("Strength of the camera jolt, on the game's own scale (a normal hit is 1.2, taking damage 1.6). Follows the game's camera shake setting. 0 turns it off.",
                    new AcceptableValueRange<float>(0f, 3f)));
            ImpactSound = Config.Bind("Impact", "Sound", "sfx_metal_blocked_overlay",
                "Game sound played when a counter lands, only for you. Empty turns it off. Other sharp ones: sfx_sword_hit, sfx_metal_blocked, sfx_HiveQueen_slash.");
            ImpactVolume = Config.Bind("Impact", "SoundVolume", 1f,
                new ConfigDescription("Volume of that sound, before the game's sound effect volume.", new AcceptableValueRange<float>(0f, 1f)));
            ImpactCrash = Config.Bind("Impact", "CrashSound", "sfx_metalbars_break",
                "Second sound under the first: a crash played slower and deeper (the game's metal bars breaking). Empty turns it off.");
            ImpactCrashPitch = Config.Bind("Impact", "CrashPitch", 0.65f,
                new ConfigDescription("Speed of the crash: below 1 is slower and deeper.", new AcceptableValueRange<float>(0.3f, 1.5f)));
            ImpactCrashSeconds = Config.Bind("Impact", "CrashSeconds", 0.85f,
                new ConfigDescription("The crash stops after this many seconds, fading out over the end, so it stays a hit. 0 plays it whole (over 3 s when slowed).", new AcceptableValueRange<float>(0f, 4f)));
            ImpactCrashVolume = Config.Bind("Impact", "CrashVolume", 1.3f,
                new ConfigDescription("Volume of the crash, before the game's sound effect volume. Above 1 it is played more than once at the same moment to get louder.", new AcceptableValueRange<float>(0f, 2f)));
            KatanaModelOn = Config.Bind("Visual", "KatanaModel", true,
                "Show the katana model. Off shows the old placeholder look (a recolored Krom or Slayer). Needs a restart.");
            ScabbardOn = Config.Bind("Visual", "Scabbard", true,
                "A scabbard at the left hip while a katana is in the hand. In the stance the blade sits in it, and a parry draws it out. Needs the katana model and the stance.");
            HolsterOn = Config.Bind("Visual", "SheathedWhenPutAway", true,
                "When you put your weapons away, the katana stays in its scabbard at the left hip instead of going on the back, and the hands are free. Needs the scabbard.");
            SlashTrailOn = Config.Bind("Visual", "SlashTrail", true,
                "The parry's cut and the thunderclap leave a bright trail, gold for the steel katana, blue lightning for the flametal one.");
            GlintOn = Config.Bind("Visual", "ParryGlint", true,
                "A white glint at the scabbard mouth the moment a perfect parry lands, before the cut.");
            SparksOn = Config.Bind("Visual", "FlametalSparks", true,
                "Lightning crackles at the flametal katana's scabbard mouth while it is sheathed in the stance.");
            ThunderOn = Config.Bind("Thunderclap", "Enabled", true,
                new ConfigDescription("The Flametal Katana's heavy attack is the thunderclap: a low crouch with the blade sheathed while it gathers, then a draw and cut while dashing forward through every creature on the way. Off gives it the steel katana's heavy attack.", null, sync));
            ThunderDamage = Config.Bind("Thunderclap", "DamageMultiplier", 10f,
                new ConfigDescription("Damage to each creature on the way, as a multiple of the katana's damage. Each takes it in full, it is not split between them.", new AcceptableValueRange<float>(1f, 20f), sync));
            ThunderPrep = Config.Bind("Thunderclap", "GatherSeconds", 2.25f,
                new ConfigDescription("How long the crouch gathers before the cut. A stagger or a dodge in that time cancels it, and the stamina is gone.", new AcceptableValueRange<float>(0.5f, 4f), sync));
            ThunderDistance = Config.Bind("Thunderclap", "Distance", 13f,
                new ConfigDescription("How far the dash goes, meters. It stops short of walls, rocks and buildings.", new AcceptableValueRange<float>(0f, 20f), sync));
            ThunderWidth = Config.Bind("Thunderclap", "Width", 1f,
                new ConfigDescription("How far to each side of the path a creature can be and still be cut, meters.", new AcceptableValueRange<float>(0.3f, 3f), sync));
            ThunderStamina = Config.Bind("Thunderclap", "Stamina", 100f,
                new ConfigDescription("Stamina the thunderclap costs, before gear and the Swords skill lower it like any attack's.", new AcceptableValueRange<float>(0f, 200f), sync));
            ThunderTrailOn = Config.Bind("Thunderclap", "LightningTrail", true,
                "Eikthyr's lightning from where the dash ends back along its path. Everyone near sees it.");
            ImpactClick = Config.Bind("Impact", "ClickSound", "sfx_metal_blocked",
                "The click when the blade is back in its scabbard after the parry's cut, heard by everyone near. Played high and cut very short. Empty turns it off.");
            ImpactClickPitch = Config.Bind("Impact", "ClickPitch", 2.2f,
                new ConfigDescription("Speed of the click: above 1 is faster and higher.", new AcceptableValueRange<float>(0.5f, 3f)));
            ImpactClickVolume = Config.Bind("Impact", "ClickVolume", 0.35f,
                new ConfigDescription("Volume of the click, before the game's sound effect volume.", new AcceptableValueRange<float>(0f, 1f)));
            // the stance only turns bones on your screen, for every player you see, so each player sets it for themselves
            var stance = new StanceSettings();
            StanceEnabled = Config.Bind("Stance", "Enabled", true,
                "Iai stance with a katana: the upper body turns left, the right hand rests on the grip and the left hand holds the scabbard at the left hip. Shown on every player you see.");
            StanceReady = Config.Bind("Stance", "ReadyIdle", true,
                "Rest in the stance whenever a katana is in the hand and you stand or walk (and run, with ReadyWhileRunning), so it sits in its scabbard. Swings, dodges and emotes leave it on their own. Off: only while blocking.");
            StanceReadyRunning = Config.Bind("Stance", "ReadyWhileRunning", true,
                "The ready stance also holds while jogging, sprinting and jumping, so the katana stays sheathed until you swing or parry. Off: only standing and walking.");
            StanceSheathe = Config.Bind("Stance", "SheatheSeconds", 0.35f,
                new ConfigDescription("Seconds to move back into the stance when coming to rest, with a gentle start and end. Blocks use BlendIn instead.", new AcceptableValueRange<float>(0f, 1.5f)));
            StanceZanshin = Config.Bind("Stance", "Zanshin", 0.6f,
                new ConfigDescription("Seconds the blade stays out after a counter, before the flick and the sheathing.", new AcceptableValueRange<float>(0f, 5f)));
            StanceNoto = Config.Bind("Stance", "SheathingSeconds", 1.5f,
                new ConfigDescription("How long the flick and the slow sheathing after fighting take. Put away waits for it.", new AcceptableValueRange<float>(0.5f, 3f)));
            StanceSlash = Config.Bind("Stance", "IaiSlash", true,
                "On a perfect parry the katana is drawn from the stance and cuts across from left to right, and the counter lands as the blade passes. Off plays the old fast swing (Swing.CounterAnimation). Needs Stance.Enabled.");
            StanceBlendIn = Config.Bind("Stance", "BlendIn", 0.04f,
                new ConfigDescription("Seconds to move into the stance when the block starts.", new AcceptableValueRange<float>(0f, 0.5f)));
            StanceBlendOut = Config.Bind("Stance", "BlendOut", 0.06f,
                new ConfigDescription("Seconds to leave it when the block ends or a swing starts.", new AcceptableValueRange<float>(0f, 0.5f)));
            StanceGripSide = Config.Bind("Stance", "GripSide", stance.Grip.x,
                new ConfigDescription("Where the right hand holds the grip, meters to the right of the body's middle (negative is left).", new AcceptableValueRange<float>(-0.4f, 0.4f)));
            StanceGripHeight = Config.Bind("Stance", "GripHeight", stance.Grip.y,
                new ConfigDescription("Height of that hand, meters above the feet of a standing player (the hips are at 0.91).", new AcceptableValueRange<float>(0.6f, 1.6f)));
            StanceGripForward = Config.Bind("Stance", "GripForward", stance.Grip.z,
                new ConfigDescription("How far in front of the body that hand is, meters.", new AcceptableValueRange<float>(-0.2f, 0.6f)));
            StanceBladeYaw = Config.Bind("Stance", "BladeYaw", stance.BladeYaw,
                new ConfigDescription("Where the blade points, degrees: 180 straight back, more turns it out to the left.", new AcceptableValueRange<float>(90f, 270f)));
            StanceBladePitch = Config.Bind("Stance", "BladePitch", stance.BladePitch,
                new ConfigDescription("How much the blade slopes down, degrees.", new AcceptableValueRange<float>(-30f, 80f)));
            StanceTwist = Config.Bind("Stance", "Twist", stance.SpineTwist,
                new ConfigDescription("Turn of the upper body, degrees (negative is to the left). The head keeps looking ahead.", new AcceptableValueRange<float>(-60f, 30f)));
            StanceLean = Config.Bind("Stance", "Lean", stance.SpineLean,
                new ConfigDescription("Forward lean of the upper body, degrees.", new AcceptableValueRange<float>(-20f, 40f)));
            StanceLeftHand = Config.Bind("Stance", "LeftHandOnBlade", stance.LeftAlongBlade,
                new ConfigDescription("Where the left hand holds the scabbard, meters along it from the right hand (the mouth is at about 0.12).", new AcceptableValueRange<float>(0.08f, 0.5f)));
            StanceSpineHold = Config.Bind("Stance", "SpineHold", 0.8f,
                new ConfigDescription("How much the stance holds the upper body upright over the game's idle, walk and run, which hunch it forward. 1 holds it fully, so the hands sit exactly where the stance puts them. 0 leaves the game's.", new AcceptableValueRange<float>(0f, 1f)));
            // the stance changed in 0.1.1: values still at 0.1.0's defaults move to the new ones, changed ones stay
            Renew(StanceGripSide, -0.1f);
            Renew(StanceGripHeight, 1.14f);
            Renew(StanceGripForward, 0.2f);
            Renew(StanceBladeYaw, 228f);
            Renew(StanceBladePitch, 24f);
            Renew(StanceTwist, -32f);
            Renew(StanceLean, 10f);
            Renew(StanceLeftHand, 0.15f);
            DumpAnimationClips = Config.Bind("Debug", "DumpAnimationClips", false,
                "Write the names of all player animation clips and the playing states to the log once, for making replacement animations.");
            LogCounters = Config.Bind("Debug", "LogCounters", false,
                "Write every counter's damage and the learned swing timings to the log. For tuning.");

            AddLocalization();
            PrefabManager.OnVanillaPrefabsAvailable += AddItems;
            new Harmony(ModGuid).PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo($"{ModName} {ModVersion} loaded");
        }

        // a setting still at an old default takes the new default
        private static void Renew(ConfigEntry<float> entry, float oldDefault)
        {
            if (Mathf.Abs(entry.Value - oldDefault) < 1e-4f) entry.Value = (float)entry.DefaultValue;
        }

        private void AddItems()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= AddItems;
            if (LogCounters.Value) LogHammerAnimations();
            AddKatana(Katana.SteelPrefab, "THSwordKrom", Katana.SteelToken, Katana.ApplySteelStats, "steel", SteelTint, "forge", 4, Katana.SteelRecipe, Katana.SteelIdol);
            AddKatana(Katana.FlametalPrefab, "THSwordSlayer", Katana.FlametalToken, Katana.ApplyFlametalStats, "flametal", FlametalTint, "blackforge", 3, Katana.FlametalRecipe, Katana.FlametalIdol);
        }

        // one katana: a clone of a vanilla two-handed sword with katana stats, the borrowed heavy animation and its recipe
        private static void AddKatana(string prefabName, string basePrefab, string token, System.Action<ItemDrop.ItemData.SharedData> applyStats,
            string look, Color tint, string station, int stationLevel, (string item, int amount, int perLevel)[] recipe, string idol)
        {
            var prefab = PrefabManager.Instance.CreateClonedPrefab(prefabName, basePrefab);
            if (!prefab)
            {
                Log.LogError($"{basePrefab} not found, {prefabName} is not added");
                return;
            }

            var shared = prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
            float basePower = shared.m_blockPower; // the base sword's, before the katana's own stats
            applyStats(shared);
            Katana.BaseBlockPower[token] = basePower;
            KatanaShared.Add(shared);
            if (prefabName == Katana.FlametalPrefab) FlametalShared.Add(shared);
            ApplyAttackSettings();
            BaseMoves[shared] = new BaseMoveset
            {
                State = shared.m_animationState,
                Attack = shared.m_attack.m_attackAnimation, AttackChain = shared.m_attack.m_attackChainLevels, AttackRandom = shared.m_attack.m_attackRandomAnimations,
                Heavy = shared.m_secondaryAttack.m_attackAnimation, HeavyChain = shared.m_secondaryAttack.m_attackChainLevels, HeavyRandom = shared.m_secondaryAttack.m_attackRandomAnimations,
            };
            ApplyMoveSettings();
            if (!KatanaModelOn.Value || !KatanaModel.Apply(prefab, look)) Tint(prefab, tint);

            foreach (var missing in recipe.Where(x => !PrefabManager.Instance.GetPrefab(x.item)))
                Log.LogWarning($"{prefabName} recipe: item '{missing.item}' does not exist in this game version");

            bool hasIdol = PrefabManager.Instance.GetPrefab(idol);
            if (!hasIdol) Log.LogWarning($"{prefabName} recipe: idol '{idol}' does not exist in this game version");
            var requirements = recipe.Select(r => new RequirementConfig(r.item, r.amount, r.perLevel, true)).ToList();
            if (hasIdol) requirements.Add(new RequirementConfig(idol, 1, 0, false)); // spent, never given back

            var config = new ItemConfig
            {
                Name = token,
                Description = token + "_desc",
                CraftingStation = station,
                MinStationLevel = stationLevel,
                Requirements = requirements.ToArray()
            };
            var icon = RenderManager.Instance.Render(new RenderManager.RenderRequest(prefab) { Rotation = RenderManager.IsometricRotation, UseCache = false });
            if (icon) config.Icons = new[] { icon };

            var item = new CustomItem(prefab, false, config);
            // the idol only counts at the Forge of Potential (refining past level 4), like on the game's own weapons
            var resources = item.Recipe?.Recipe?.m_resources;
            if (hasIdol && resources != null && resources.Length > 0) resources[resources.Length - 1].m_upgraderResource = true;
            ItemManager.Instance.AddItem(item);
        }

        // each katana's own base weapon moves (Krom's, the Slayer's), to go back to when a swing setting is off
        private struct BaseMoveset
        {
            internal ItemDrop.ItemData.AnimationState State;
            internal string Attack, Heavy;
            internal int AttackChain, AttackRandom, HeavyChain, HeavyRandom;
        }

        private static readonly Dictionary<ItemDrop.ItemData.SharedData, BaseMoveset> BaseMoves = new Dictionary<ItemDrop.ItemData.SharedData, BaseMoveset>();

        // the swing settings on every katana's item data: the upper body swings or the greatsword ones, the one-handed or
        // the greatsword movement, the heavy attack's motion. again whenever a value changes or the server's arrives
        private static void ApplyMoveSettings()
        {
            Moves.OwnCuts = UpperBodySwings.Value;
            foreach (var pair in BaseMoves)
            {
                var shared = pair.Key;
                var own = pair.Value;
                var normal = shared.m_attack;
                var heavy = shared.m_secondaryAttack;
                shared.m_animationState = OneHandedMoves.Value ? ItemDrop.ItemData.AnimationState.OneHanded : own.State;
                // the game builds the trigger from the name and the chain level: shovel0, shovel1, shovel2
                normal.m_attackAnimation = UpperBodySwings.Value ? UpperSwing : own.Attack;
                normal.m_attackChainLevels = UpperBodySwings.Value ? 3 : own.AttackChain;
                normal.m_attackRandomAnimations = UpperBodySwings.Value ? 0 : own.AttackRandom;
                heavy.m_attackAnimation = own.Heavy;
                heavy.m_attackChainLevels = own.HeavyChain;
                heavy.m_attackRandomAnimations = own.HeavyRandom;
                if (UpperBodySwings.Value && HeavyAnimationFrom.Value == "UpperBody")
                {
                    heavy.m_attackAnimation = UpperSwing + "2";
                    heavy.m_attackChainLevels = 1;
                    heavy.m_attackRandomAnimations = 0;
                    continue;
                }
                // a borrowed motion, UpperBody without the upper body swings is 0.1.1's Stagbreaker slam
                string from = HeavyAnimationFrom.Value == "UpperBody" ? "SledgeStagbreaker" : HeavyAnimationFrom.Value;
                if (!BorrowHeavyAnimation(shared, from) && from != "SledgeIron") BorrowHeavyAnimation(shared, "SledgeIron");
            }
        }

        // every katana of a kind shares its item data, so a change or a server sync applies to the next swing
        private static void ApplyAttackSettings()
        {
            foreach (var shared in KatanaShared)
            {
                var normal = shared.m_attack;
                var heavy = shared.m_secondaryAttack;
                if (normal == null || heavy == null) continue;
                bool flametal = FlametalShared.Contains(shared);
                heavy.m_damageMultiplier = HeavyDamageMultiplier.Value;
                heavy.m_attackStamina = flametal ? FlametalHeavyStamina.Value : HeavyStamina.Value;
                if (flametal) normal.m_attackStamina = FlametalSwingStamina.Value;
                normal.m_attackRange = NormalRange.Value;
                normal.m_attackAngle = NormalAngle.Value;
                normal.m_attackRayWidth = NormalRayWidth.Value;
                heavy.m_attackRange = HeavyRange.Value;
                heavy.m_attackAngle = HeavyAngle.Value;
                heavy.m_attackRayWidth = HeavyRayWidth.Value;
            }
        }

        // lists both attacks of the two-handed hammers and the battleaxe, to pick an animation from
        private static void LogHammerAnimations()
        {
            foreach (var name in new[] { "SledgeStagbreaker", "SledgeIron", "SledgeDemolisher", "Battleaxe" })
            {
                var prefab = PrefabManager.Instance.GetPrefab(name);
                var s = prefab ? prefab.GetComponent<ItemDrop>()?.m_itemData.m_shared : null;
                Log.LogInfo(s == null ? $"{name}: not found"
                    : $"{name}: primary '{s.m_attack?.m_attackAnimation}' (chain {s.m_attack?.m_attackChainLevels}), secondary '{s.m_secondaryAttack?.m_attackAnimation}' (chain {s.m_secondaryAttack?.m_attackChainLevels}, type {s.m_secondaryAttack?.m_attackType})");
            }
        }

        // the secondary attack plays another weapon's attack motion instead of krom's thrust.
        // damage, stamina and the hit area stay those of krom's thrust, so it stays a slash
        private static bool BorrowHeavyAnimation(ItemDrop.ItemData.SharedData shared, string weaponPrefab)
        {
            var weapon = PrefabManager.Instance.GetPrefab(weaponPrefab);
            var sourceShared = weapon ? weapon.GetComponent<ItemDrop>()?.m_itemData.m_shared : null;
            var source = sourceShared == null ? null
                : HeavyAnimationSlot.Value == "Secondary" && !string.IsNullOrEmpty(sourceShared.m_secondaryAttack?.m_attackAnimation)
                    ? sourceShared.m_secondaryAttack : sourceShared.m_attack;
            if (source == null || string.IsNullOrEmpty(source.m_attackAnimation))
            {
                Log.LogWarning($"{weaponPrefab} not found, the heavy attack cannot borrow its animation");
                return false;
            }
            var own = shared.m_secondaryAttack;
            if (LogCounters.Value)
                Log.LogInfo($"heavy attack: krom thrust '{own.m_attackAnimation}' becomes {weaponPrefab} {(source == sourceShared.m_secondaryAttack ? "secondary" : "primary")} '{source.m_attackAnimation}' (chain {source.m_attackChainLevels}, random {source.m_attackRandomAnimations})");
            // the game builds the animation trigger from these three, so they must all match the source
            own.m_attackAnimation = source.m_attackAnimation;
            own.m_attackChainLevels = source.m_attackChainLevels;
            own.m_attackRandomAnimations = source.m_attackRandomAnimations;
            return true;
        }

        private static void Tint(GameObject prefab, Color tint)
        {
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                renderer.sharedMaterials = renderer.sharedMaterials
                    .Select(m => m && m.HasProperty("_Color") ? new Material(m) { name = m.name + "_iaido", color = m.color * tint } : m)
                    .ToArray();
        }

        private static void AddLocalization()
        {
            string english = "English";
            LocalizationManager.Instance.GetLocalization().AddTranslation(english, new Dictionary<string, string>
            {
                ["item_iaidokatana_steel"] = "Steel Katana",
                ["item_iaidokatana_steel_desc"] = "Folded steel in a lacquered scabbard. Weak in an open fight. A perfect parry strikes back on its own. Its parry window is slightly tighter than other weapons'.",
                ["item_iaidokatana_flametal"] = "Flametal Katana",
                ["item_iaidokatana_flametal_desc"] = "Flametal folded around an iolite core. Lightning runs along the edge. A perfect parry strikes back on its own. Its parry window is slightly tighter than other weapons'."
            });
        }
    }
}
