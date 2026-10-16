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
        public const string ModVersion = "0.3.1";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> CounterEnabled;
        internal static ConfigEntry<float> CounterMultiplier;
        internal static ConfigEntry<bool> CounterArea;
        internal static ConfigEntry<float> CounterAreaMultiplier;
        internal static ConfigEntry<float> CounterAreaAngle;
        internal static ConfigEntry<float> CounterAreaRange;
        internal static ConfigEntry<bool> AfterimageOn;
        internal static ConfigEntry<string> ThunderAfterimage;
        internal static ConfigEntry<float> ChargeSoundVolume;
        internal static ConfigEntry<bool> ChargeBallOn;
        internal static ConfigEntry<bool> ChopOn;
        internal static ConfigEntry<float> SteelChop;
        internal static ConfigEntry<int> SteelChopTier;
        internal static ConfigEntry<float> FlametalChop;
        internal static ConfigEntry<int> FlametalChopTier;
        internal static ConfigEntry<float> BlockBonusMax;
        internal static ConfigEntry<float> ReflectMultiplier;
        internal static ConfigEntry<float> CounterDelay;
        internal static ConfigEntry<bool> CounterInPvP;
        internal static ConfigEntry<int> ParryWindowMs;
        internal static ConfigEntry<int> ParryNormalMs;
        internal static ConfigEntry<bool> ParryPrecise;
        internal static ConfigEntry<float> ParryPreciseBonus;
        internal static ConfigEntry<bool> ParryNoStagger;
        internal static ConfigEntry<bool> SwingTimeWarp;
        internal static ConfigEntry<float> WindupShare;
        internal static ConfigEntry<float> WindupSpeed;
        internal static ConfigEntry<float> HeavySpeed;
        internal static ConfigEntry<float> NormalStrikeBoost;
        internal static ConfigEntry<float> HeavyStrikeBoost;
        internal static ConfigEntry<float> HeavyDuration;
        internal static ConfigEntry<float> HeavyDamageMultiplier;
        internal static ConfigEntry<float> HeavyStamina;
        internal static ConfigEntry<float> SwingStamina;
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
        internal static ConfigEntry<bool> CustomAnimations;
        internal static ConfigEntry<float> FullBodyStrikeBoost;
        internal static ConfigEntry<float> SlashSoundVolume;
        internal static ConfigEntry<bool> SlashMarkOn;
        internal static ConfigEntry<float> SlashShake;
        internal static ConfigEntry<bool> OneHandedMoves;
        internal const string UpperSwing = "shovel"; // the game's upper body swing chain (the snow shovel's): shovel0, 1, 2
        internal static ConfigEntry<string> HeavyAnimationSlot;
        internal static ConfigEntry<bool> CounterAnimation;
        internal static ConfigEntry<float> CounterAnimationSpeed;
        internal static ConfigEntry<bool> LogCounters;
        internal static ConfigEntry<bool> RecordPoses;
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
        internal static ConfigEntry<string> ImpactExtraSounds;
        internal static ConfigEntry<float> ParryClankVolume;
        internal static ConfigEntry<bool> OwnParrySoundsOn;
        internal static ConfigEntry<float> OwnParrySoundVolume;
        internal static ConfigEntry<float> ParrySoundVolume;
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
        internal static ConfigEntry<float> StanceTorsoHold;
        internal static ConfigEntry<float> StanceShoulderHold;
        internal static ConfigEntry<bool> StanceSlash;
        internal static ConfigEntry<bool> StanceReady;
        internal static ConfigEntry<bool> StanceReadyRunning;
        internal static ConfigEntry<float> StanceSheathe;
        internal static ConfigEntry<float> StanceZanshin;
        internal static ConfigEntry<float> StanceNoto;

        internal static Plugin Instance;

        // fine tuning values, bound in memory only at their defaults and never written, so the config file holds only
        // what players and servers change
        private static ConfigFile _fixed;
        // the version that last wrote the config file, null for a new file
        private static System.Version _configFrom;

        // placeholder look until the real model: krom in a darker steel
        private static readonly Color SteelTint = new Color(0.62f, 0.64f, 0.72f);
        // placeholder look of the ashlands katana: slayer with a cold blue cast for the lightning
        private static readonly Color FlametalTint = new Color(0.7f, 0.78f, 1f);

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            var sync = new ConfigurationManagerAttributes { IsAdminOnly = true };
            // read before the first setting rewrites the file, so each move to a new default runs only once
            _configFrom = ConfigVersion(Config.ConfigFilePath);
            Logger.LogInfo($"config file last written by {(_configFrom == null ? "no version (new file)" : "version " + _configFrom)}");
            _fixed = new ConfigFile(Path.Combine(BepInEx.Paths.ConfigPath, ModGuid + ".fixed"), false) { SaveOnConfigSet = false };

            CounterEnabled = Config.Bind("Counter", "Enabled", true,
                new ConfigDescription("A perfect parry with a katana strikes the attacker back on its own.", null, sync));
            CounterMultiplier = Config.Bind("Counter", "DamageMultiplier", 3f,
                new ConfigDescription("Counter damage on the parried attacker as a multiple of the katana's damage, before Block skill. 2.5 hits like a Black Metal sword.",
                    new AcceptableValueRange<float>(0.5f, 10f), sync));
            // 0.2.1: the counter hits harder (the user), a value still at 0.2.0's default moves up
            Renew(CounterMultiplier, 2.5f, "0.2.1");
            CounterArea = Config.Bind("Counter", "AreaHit", true,
                new ConfigDescription("The counter also cuts every other enemy within a wide swing toward the parried attacker (Counter.AreaRange and AreaAngle), each in full. Melee parries only.", null, sync));
            CounterAreaMultiplier = Config.Bind("Counter", "AreaDamageMultiplier", 2f,
                new ConfigDescription("Damage of that cut on the other enemies, as a multiple of the katana's damage (1 is a normal swing's hit). Each enemy takes it at most once, also when several are parried at the same moment.",
                    new AcceptableValueRange<float>(0f, 10f), sync));
            Renew(CounterAreaMultiplier, 1f, "0.2.1"); // the first 0.2.1 test build had 1
            CounterAreaAngle = Config.Bind("Counter", "AreaAngle", 170f,
                new ConfigDescription("Width of the counter's cut around the player, in degrees, centered on the parried attacker (170 reaches 85 degrees to each side).",
                    new AcceptableValueRange<float>(30f, 360f), sync));
            CounterAreaRange = Config.Bind("Counter", "AreaRange", 4f,
                new ConfigDescription("How far the counter's cut reaches from the player, meters (up and down it reaches 3 m). Trees, logs and stumps in it are chopped too.",
                    new AcceptableValueRange<float>(1f, 8f), sync));
            ChopOn = Config.Bind("Chop", "Enabled", true,
                new ConfigDescription("The counter's cut and the Thunderclap's dash also chop trees, logs and stumps in their reach, like an axe.", null, sync));
            SteelChop = _fixed.Bind("Chop", "SteelChop", 50f,
                new ConfigDescription("Chop damage of the Steel Katana's counter cut (the Crystal Battleaxe has 50).", new AcceptableValueRange<float>(0f, 200f), sync));
            SteelChopTier = _fixed.Bind("Chop", "SteelToolTier", 3,
                new ConfigDescription("Tool tier of that chop: trees that need a higher tier take nothing (the Crystal Battleaxe has 3).", new AcceptableValueRange<int>(0, 10), sync));
            FlametalChop = _fixed.Bind("Chop", "FlametalChop", 60f,
                new ConfigDescription("Chop damage of the Flametal Katana's counter cut and Thunderclap dash (the Black Metal Axe has 60).", new AcceptableValueRange<float>(0f, 200f), sync));
            FlametalChopTier = _fixed.Bind("Chop", "FlametalToolTier", 4,
                new ConfigDescription("Tool tier of that chop (the Black Metal Axe has 4).", new AcceptableValueRange<int>(0, 10), sync));
            BlockBonusMax = Config.Bind("Counter", "BlockSkillBonus", 1.5f,
                new ConfigDescription("Extra counter damage from Block skill: 1x at skill 0, rising to this at skill 100.",
                    new AcceptableValueRange<float>(1f, 3f), sync));
            ReflectMultiplier = Config.Bind("Counter", "ReflectMultiplier", 0.5f,
                new ConfigDescription("Share of the counter that a parried arrow, spear or spell sends to the shooter.",
                    new AcceptableValueRange<float>(0f, 1f), sync));
            CounterDelay = _fixed.Bind("Counter", "Delay", 0.1f,
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
            ParryNoStagger = Config.Bind("Parry", "NoStagger", true,
                new ConfigDescription("A katana parry never staggers you. The parried hit adds nothing to your stagger bar, and the bar empties. Running out of stamina still breaks the parry. Turned off, the game's stagger applies as before.", null, sync));
            SwingTimeWarp = _fixed.Bind("Swing", "TimeWarp", true,
                "Times katana swings so their hit lands when the greatsword's did. With Swing.UpperBody an even pace up to the hit, without it a slow windup and a fast strike. Only changes how your own swings look.");
            WindupShare = _fixed.Bind("Swing", "WindupShare", 0.5f,
                new ConfigDescription("Only with Swing.UpperBody off. Share of the animation before the hit that plays as the slow windup. Must stay below WindupSpeed.", new AcceptableValueRange<float>(0.1f, 0.9f)));
            WindupSpeed = _fixed.Bind("Swing", "WindupSpeed", 0.6f,
                new ConfigDescription("Only with Swing.UpperBody off. Animation speed during the windup. The strike speeds up to make up the time (3x with the defaults).", new AcceptableValueRange<float>(0.3f, 1f)));
            NormalStrikeBoost = _fixed.Bind("Swing", "NormalStrikeBoost", 1.875f,
                new ConfigDescription("Only with Swing.UpperBody off. Makes the strike of normal swings this much faster. The windup slows down so the hit lands at the same moment as vanilla.", new AcceptableValueRange<float>(1f, 3f)));
            HeavyStrikeBoost = _fixed.Bind("Swing", "HeavyStrikeBoost", 1.3f,
                new ConfigDescription("Only for a borrowed heavy motion (HeavyAnimationFrom other than UpperBody). Makes the slash of the heavy attack this much faster. Its raise slows down so the hit lands at the same moment.", new AcceptableValueRange<float>(1f, 3f)));
            HeavyDuration = _fixed.Bind("Swing", "HeavyDuration", 2f,
                new ConfigDescription("Total length of the heavy attack in seconds, recovery included. Only the raise changes to fit, the slash keeps its speed. 0 turns this off.", new AcceptableValueRange<float>(0f, 4f)));
            HeavyDamageMultiplier = Config.Bind("Counter", "HeavyDamageMultiplier", 6f,
                new ConfigDescription("Damage of the heavy attack as a multiple of the katana's damage (Krom's thrust is 3).", new AcceptableValueRange<float>(1f, 10f), sync));
            HeavyDamageMultiplier.SettingChanged += (s, e) => ApplyAttackSettings();
            HeavyStamina = Config.Bind("Counter", "HeavyStamina", 40f,
                new ConfigDescription("Stamina cost of the Steel Katana's heavy attack (Krom's thrust is 40), so it cannot be spammed.", new AcceptableValueRange<float>(0f, 100f), sync));
            HeavyStamina.SettingChanged += (s, e) => ApplyAttackSettings();
            // a heavy stamina still at the old default moves to the new one
            Renew(HeavyStamina, 50f, "0.2.2");
            SwingStamina = Config.Bind("Counter", "SwingStamina", 16f,
                new ConfigDescription("Stamina cost of the Steel Katana's normal swings (Krom's are 20).", new AcceptableValueRange<float>(0f, 100f), sync));
            SwingStamina.SettingChanged += (s, e) => ApplyAttackSettings();
            // the flametal katana hits much harder, so its swings cost more (25% over the steel one)
            FlametalSwingStamina = Config.Bind("Counter", "FlametalSwingStamina", 20f,
                new ConfigDescription("Stamina cost of the Flametal Katana's normal swings (SwingStamina is the Steel Katana's).", new AcceptableValueRange<float>(0f, 100f), sync));
            FlametalSwingStamina.SettingChanged += (s, e) => ApplyAttackSettings();
            Renew(FlametalSwingStamina, 24f, "0.2.2");
            FlametalHeavyStamina = Config.Bind("Counter", "FlametalHeavyStamina", 60f,
                new ConfigDescription("Stamina cost of the Flametal Katana's heavy attack (HeavyStamina is the Steel Katana's).", new AcceptableValueRange<float>(0f, 100f), sync));
            FlametalHeavyStamina.SettingChanged += (s, e) => ApplyAttackSettings();
            // which swings the katanas play is set by the server, so every player sees the same, and applied when it arrives.
            // the katana's own swings and the one-handed movement are experimental, off by default (12 Oct: they felt
            // weightless and cut off from the body)
            UpperBodySwings = _fixed.Bind("Swing", "UpperBody", false,
                new ConfigDescription("Experimental, work in progress. Katana swings play on the game's upper body animation layer, so the legs keep walking during a swing, and the katana's own cuts show over it. Off: the greatsword swings. Set by the server in multiplayer.", null, sync));
            // 0.2.0: the katana's own full-body animations (the three cuts and the heavy, clips made in Unity), on by
            // default. they replace the two test switches FullBodyCuts and ClipTest of 0.1.3
            CustomAnimations = Config.Bind("Swing", "CustomAnimations", true,
                new ConfigDescription("The katana's own full-body animations for the three cuts and the heavy attack. Off: the older animations borrowed from Krom (the swings) and Stagbreaker (the heavy). Set by the server in multiplayer.", null, sync));
            FullBodyStrikeBoost = _fixed.Bind("Swing", "FullBodyStrikeBoost", 1f,
                new ConfigDescription("Only for the katana's own animations (Swing.CustomAnimations). Like NormalStrikeBoost: 1 strikes in about 80 ms after a slow windup, higher strikes faster.", new AcceptableValueRange<float>(1f, 3f)));
            CustomAnimations.SettingChanged += (s, e) => ApplyMoveSettings();
            OneHandedMoves = _fixed.Bind("Swing", "OneHandedMoves", false,
                new ConfigDescription("Experimental, work in progress. The katana uses the one-handed sword's idle, walk, run and block animations (it stays a two-handed weapon). Set by the server in multiplayer.", null, sync));
            HeavyAnimationFrom = _fixed.Bind("Swing", "HeavyAnimationFrom", "SledgeStagbreaker",
                new ConfigDescription("Weapon whose heavy motion the heavy attack borrows, by prefab name (for example SledgeStagbreaker, SledgeIron), with its own legs. The hit stays the katana's own slash. UpperBody (experimental, with Swing.UpperBody on): the katana's own overhead cut. Set by the server in multiplayer.", null, sync));
            HeavyAnimationSlot = _fixed.Bind("Swing", "HeavyAnimationSlot", "Secondary",
                new ConfigDescription("Only for a borrowed heavy motion. Which of that weapon's attacks to borrow. On the two-handed hammers the secondary is the overhead ground slam. Set by the server in multiplayer.",
                    new AcceptableValueList<string>("Secondary", "Primary"), sync));
            UpperBodySwings.SettingChanged += (s, e) => ApplyMoveSettings();
            OneHandedMoves.SettingChanged += (s, e) => ApplyMoveSettings();
            HeavyAnimationFrom.SettingChanged += (s, e) => ApplyMoveSettings();
            HeavyAnimationSlot.SettingChanged += (s, e) => ApplyMoveSettings();
            HeavySpeed = _fixed.Bind("Swing", "HeavySpeed", 1.7f,
                new ConfigDescription("Only for a borrowed heavy motion. Speeds up the heavy (secondary) attack up to its hit. The recovery after the hit plays at normal speed.", new AcceptableValueRange<float>(1f, 3f)));
            CounterAnimation = _fixed.Bind("Swing", "CounterAnimation", true,
                "Play a fast swing on a parry, and land the counter on its hit moment. A placeholder until the iai animation exists.");
            CounterAnimationSpeed = _fixed.Bind("Swing", "CounterAnimationSpeed", 5.5f,
                new ConfigDescription("Speed of that counter swing.", new AcceptableValueRange<float>(1f, 8f)));
            // hit area of the swings. krom: normal 2.6 m, 90 degrees, 0.5 ray. thrust 3 m, 30 degrees, 0.5 ray
            NormalRange = _fixed.Bind("Hitbox", "NormalRange", 3.0f,
                new ConfigDescription("Reach of normal swings in meters (Krom 2.6).", new AcceptableValueRange<float>(1.5f, 6f), sync));
            NormalAngle = _fixed.Bind("Hitbox", "NormalAngle", 110f,
                new ConfigDescription("Width of normal swings in degrees (Krom 90).", new AcceptableValueRange<float>(10f, 180f), sync));
            NormalRayWidth = _fixed.Bind("Hitbox", "NormalRayWidth", 0.7f,
                new ConfigDescription("Thickness of normal swings in meters (Krom 0.5).", new AcceptableValueRange<float>(0.1f, 2f), sync));
            HeavyRange = _fixed.Bind("Hitbox", "HeavyRange", 4.0f,
                new ConfigDescription("Reach of the heavy attack in meters (Krom's thrust 3).", new AcceptableValueRange<float>(1.5f, 8f), sync));
            HeavyAngle = _fixed.Bind("Hitbox", "HeavyAngle", 45f,
                new ConfigDescription("Width of the heavy attack in degrees with the older borrowed animations (Krom's thrust 30). The katana's own heavy (Swing.CustomAnimations) cuts from high to low, a bit wider than the torso. Its chop takes this width.", new AcceptableValueRange<float>(5f, 180f), sync));
            HeavyRayWidth = _fixed.Bind("Hitbox", "HeavyRayWidth", 0.9f,
                new ConfigDescription("Thickness of the heavy attack in meters with the older borrowed animations (Krom's thrust 0.5). The katana's own heavy (Swing.CustomAnimations) is torso wide.", new AcceptableValueRange<float>(0.1f, 2f), sync));
            foreach (var entry in new[] { NormalRange, NormalAngle, NormalRayWidth, HeavyRange, HeavyAngle, HeavyRayWidth })
                entry.SettingChanged += (s, e) => ApplyAttackSettings();
            // the impact frame is only on your own screen, so every player sets it for themselves
            ImpactEnabled = Config.Bind("Impact", "Enabled", true,
                "Impact frame when a counter lands: a short black and white flash, a camera jolt and a sharp sound. Only on your own screen.");
            ImpactFlashSeconds = _fixed.Bind("Impact", "FlashSeconds", 0.07f,
                new ConfigDescription("How long the screen stays black and white and white-flashed, in real seconds (0.07 is about 4 frames at 60 fps). 0 turns the flash off.",
                    new AcceptableValueRange<float>(0f, 0.3f)));
            ImpactContrast = _fixed.Bind("Impact", "FlashContrast", 1.5f,
                new ConfigDescription("Contrast during the flash, as a multiple of the game's own.", new AcceptableValueRange<float>(1f, 1.65f)));
            ImpactWhite = _fixed.Bind("Impact", "WhiteFlash", 0.45f,
                new ConfigDescription("Strength of a white flash over the whole screen, held for FlashSeconds and then faded out. With the ink frame it comes right after it. 0 turns it off.", new AcceptableValueRange<float>(0f, 1f)));
            ImpactInk = Config.Bind("Impact", "InkFrame", true,
                "Ink frame: the moment of the cut freezes as a stark black and white picture, like an anime impact frame. Only on your own screen.");
            ImpactInkSeconds = _fixed.Bind("Impact", "InkSeconds", 0.075f,
                new ConfigDescription("How long the ink frame stays, in real seconds.", new AcceptableValueRange<float>(0f, 0.5f)));
            ImpactInkInvert = _fixed.Bind("Impact", "InkInvert", false,
                "Swap black and white in the ink frame (bright things turn black).");
            ImpactInkCut = _fixed.Bind("Impact", "InkCut", true,
                "Draw the cut as a sharp streak through the enemy in the ink frame.");
            ImpactFreeze = _fixed.Bind("Impact", "FreezeSeconds", 0.15f,
                new ConfigDescription("How long both fighters freeze when a counter lands, the game's own hit stop (a normal hit is much shorter).", new AcceptableValueRange<float>(0f, 0.4f)));
            ImpactShake = Config.Bind("Impact", "Shake", 1.7f,
                new ConfigDescription("Strength of the camera jolt, on the game's own scale (a normal hit is 1.2, taking damage 1.6). Follows the game's camera shake setting. 0 turns it off.",
                    new AcceptableValueRange<float>(0f, 3f)));
            ImpactSound = _fixed.Bind("Impact", "Sound", "sfx_metal_blocked_overlay",
                "Game sound played when a counter lands, only for you. Empty turns it off. Other sharp ones: sfx_sword_hit, sfx_metal_blocked, sfx_HiveQueen_slash.");
            ImpactVolume = _fixed.Bind("Impact", "SoundVolume", 0.42f,
                new ConfigDescription("Volume of that sound, before the game's sound effect volume.", new AcceptableValueRange<float>(0f, 1f)));
            ImpactCrash = _fixed.Bind("Impact", "CrashSound", "",
                "Second sound under the first: a crash played slower and deeper (for example sfx_metalbars_break, the game's metal bars breaking). Empty turns it off.");
            ImpactCrashPitch = _fixed.Bind("Impact", "CrashPitch", 0.65f,
                new ConfigDescription("Speed of the crash: below 1 is slower and deeper.", new AcceptableValueRange<float>(0.3f, 1.5f)));
            ImpactCrashSeconds = _fixed.Bind("Impact", "CrashSeconds", 0.85f,
                new ConfigDescription("The crash stops after this many seconds, fading out over the end, so it stays a hit. 0 plays it whole (over 3 s when slowed).", new AcceptableValueRange<float>(0f, 4f)));
            ImpactCrashVolume = _fixed.Bind("Impact", "CrashVolume", 1.3f,
                new ConfigDescription("Volume of the crash, before the game's sound effect volume. Above 1 it is played more than once at the same moment to get louder.", new AcceptableValueRange<float>(0f, 2f)));
            ImpactExtraSounds = _fixed.Bind("Impact", "ExtraSounds", ImpactFrame.DefaultExtraSounds,
                "More game sounds played when a counter lands, only for you, as Name:pitch:volume:seconds separated by commas. Pitch 0 keeps the sound's own pitch, a range like 2.8-3 picks one each time, seconds 0 plays it whole. Empty turns them off.");
            OwnParrySoundsOn = Config.Bind("Impact", "OwnParrySounds", true,
                "Play your own sounds when a counter lands: .wav files (16 bit) in BepInEx/config/" + OwnParrySounds.FolderName + ", one picked at random each time. With no files there, the mod's own parry sound plays. Only for you.");
            OwnParrySoundVolume = Config.Bind("Impact", "OwnParrySoundVolume", 1f,
                new ConfigDescription("Volume of your own parry sounds, before the game's sound effect volume.", new AcceptableValueRange<float>(0f, 1f)));
            ParrySoundVolume = Config.Bind("Impact", "ParrySoundVolume", 1f,
                new ConfigDescription("Volume of the mod's own parry sound when a counter lands (and of the thunderclap's first hit), before the game's sound effect volume. 0 turns it off. Your own parry sounds have OwnParrySoundVolume.", new AcceptableValueRange<float>(0f, 2f)));
            ParryClankVolume = _fixed.Bind("Impact", "ParryClankVolume", 0.42f,
                new ConfigDescription("Volume of a katana's block clank when it parries, against the game's own (1). Yours, for every katana parry you hear.", new AcceptableValueRange<float>(0f, 1f)));
            ParryClankVolume.SettingChanged += (s, e) => ParryClank.ApplyVolume();
            KatanaModelOn = _fixed.Bind("Visual", "KatanaModel", true,
                "Show the katana model. Off shows the old placeholder look (a recolored Krom or Slayer). Needs a restart.");
            ScabbardOn = Config.Bind("Visual", "Scabbard", true,
                "A scabbard at the left hip while a katana is in the hand. In the stance the blade sits in it, and a parry draws it out. Needs the katana model and the stance.");
            HolsterOn = Config.Bind("Visual", "SheathedWhenPutAway", true,
                "When you put your weapons away, the katana stays in its scabbard at the left hip instead of going on the back, and the hands are free. Needs the scabbard.");
            SlashTrailOn = Config.Bind("Visual", "SlashTrail", true,
                "The parry's cut and the thunderclap leave a bright trail, gold for the steel katana, blue lightning for the flametal one.");
            SlashSoundVolume = Config.Bind("Visual", "SlashSoundVolume", 1f,
                new ConfigDescription("Volume of the katana's own whoosh as each cut's strike begins (Swing.CustomAnimations). 0 turns it off.", new AcceptableValueRange<float>(0f, 3f)));
            SlashMarkOn = Config.Bind("Visual", "SlashMark", true,
                "When a full-body cut lands, a thin line of light along the cut stays a moment and fades. Only on your own screen.");
            SlashShake = Config.Bind("Visual", "SlashShake", 0.6f,
                new ConfigDescription("Camera jolt when a full-body cut lands, on your own screen. 0 turns it off.", new AcceptableValueRange<float>(0f, 3f)));
            GlintOn = Config.Bind("Visual", "ParryGlint", true,
                "A white glint at the scabbard mouth the moment a perfect parry lands, before the cut.");
            AfterimageOn = Config.Bind("Visual", "ParryAfterimage", true,
                "The parry's cut leaves a wide arc of light around the player for a moment, gold for the steel katana, blue white lightning for the flametal one, as wide and far as the cut reaches (Counter.AreaAngle, AreaRange). Yours, for every player you see.");
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
            ThunderWidth = Config.Bind("Thunderclap", "Width", 2f,
                new ConfigDescription("How far to each side of the path a creature can be and still be cut, meters (the blade sweeps from 1.6 m left to 2 m right of the body).", new AcceptableValueRange<float>(0.3f, 3f), sync));
            Renew(ThunderWidth, 1f, "0.3.0"); // a width still at the old default moves to the new one
            ThunderStamina = Config.Bind("Thunderclap", "Stamina", 100f,
                new ConfigDescription("Stamina the thunderclap costs, before gear and the Swords skill lower it like any attack's.", new AcceptableValueRange<float>(0f, 200f), sync));
            ThunderTrailOn = Config.Bind("Thunderclap", "LightningTrail", true,
                "Eikthyr's lightning from where the dash ends back along its path. Everyone near sees it.");
            ChargeSoundVolume = Config.Bind("Thunderclap", "ChargeSoundVolume", 1f,
                new ConfigDescription("Volume of the gathering's sound (a rising electric hum, crackles, a charge and a scream, a thunder crack on the cut), before the game's sound effect volume. 0 turns it off. Yours.", new AcceptableValueRange<float>(0f, 2f)));
            ThunderAfterimage = Config.Bind("Thunderclap", "Afterimage", "Path",
                new ConfigDescription("What the cut leaves behind when the dash ends: Path, a long streak of blue white light along the whole dash. Arc, an arc of light like the parry's in front of where the dash ended, where the cut also reaches. Both, or Off. Yours, for every player you see.",
                    new AcceptableValueList<string>("Path", "Arc", "Both", "Off")));
            ChargeBallOn = Config.Bind("Thunderclap", "ChargeBall", true,
                "A ball of lightning grows at the hands while the crouch gathers and bursts on the cut. Yours, for every player you see.");
            ImpactClick = _fixed.Bind("Impact", "ClickSound", "sfx_metal_blocked",
                "The click when the blade is back in its scabbard after the parry's cut, heard by everyone near. Played high and cut very short. Empty turns it off.");
            ImpactClickPitch = _fixed.Bind("Impact", "ClickPitch", 2.2f,
                new ConfigDescription("Speed of the click: above 1 is faster and higher.", new AcceptableValueRange<float>(0.5f, 3f)));
            ImpactClickVolume = _fixed.Bind("Impact", "ClickVolume", 0.35f,
                new ConfigDescription("Volume of the click, before the game's sound effect volume.", new AcceptableValueRange<float>(0f, 1f)));
            // the stance only turns bones on your screen, for every player you see, so each player sets it for themselves
            var stance = new StanceSettings();
            StanceEnabled = Config.Bind("Stance", "Enabled", true,
                "Iai stance with a katana: the upper body turns left, the right hand rests on the grip and the left hand holds the scabbard at the left hip. Shown on every player you see.");
            StanceReady = _fixed.Bind("Stance", "ReadyIdle", true,
                "Rest in the stance whenever a katana is in the hand and you stand or walk (and run, with ReadyWhileRunning), so it sits in its scabbard. Swings, dodges and emotes leave it on their own. Off: only while blocking.");
            StanceReadyRunning = _fixed.Bind("Stance", "ReadyWhileRunning", true,
                "The ready stance also holds while jogging, sprinting and jumping, so the katana stays sheathed until you swing or parry. Off: only standing and walking.");
            StanceSheathe = _fixed.Bind("Stance", "SheatheSeconds", 0.35f,
                new ConfigDescription("Seconds to move back into the stance when coming to rest, with a gentle start and end. Blocks use BlendIn instead.", new AcceptableValueRange<float>(0f, 1.5f)));
            StanceZanshin = _fixed.Bind("Stance", "Zanshin", 0.6f,
                new ConfigDescription("Seconds the blade stays out after a counter, before the flick and the sheathing.", new AcceptableValueRange<float>(0f, 5f)));
            StanceNoto = _fixed.Bind("Stance", "SheathingSeconds", 1.5f,
                new ConfigDescription("How long the flick and the slow sheathing after fighting take. Put away waits for it.", new AcceptableValueRange<float>(0.5f, 3f)));
            StanceSlash = _fixed.Bind("Stance", "IaiSlash", true,
                "On a perfect parry the katana is drawn from the stance and cuts across from left to right, and the counter lands as the blade passes. Off plays the old fast swing (Swing.CounterAnimation). Needs Stance.Enabled.");
            StanceBlendIn = _fixed.Bind("Stance", "BlendIn", 0.04f,
                new ConfigDescription("Seconds to move into the stance when the block starts.", new AcceptableValueRange<float>(0f, 0.5f)));
            StanceBlendOut = _fixed.Bind("Stance", "BlendOut", 0.12f,
                new ConfigDescription("Seconds to leave it when the block ends, a roll or a swing starts.", new AcceptableValueRange<float>(0f, 0.5f)));
            StanceGripSide = _fixed.Bind("Stance", "GripSide", stance.Grip.x,
                new ConfigDescription("Where the right hand holds the grip, meters to the right of the body's middle (negative is left).", new AcceptableValueRange<float>(-0.4f, 0.4f)));
            StanceGripHeight = _fixed.Bind("Stance", "GripHeight", stance.Grip.y,
                new ConfigDescription("Height of that hand, meters above the feet of a standing player (the hips are at 0.91).", new AcceptableValueRange<float>(0.6f, 1.6f)));
            StanceGripForward = _fixed.Bind("Stance", "GripForward", stance.Grip.z,
                new ConfigDescription("How far in front of the body that hand is, meters.", new AcceptableValueRange<float>(-0.2f, 0.6f)));
            StanceBladeYaw = _fixed.Bind("Stance", "BladeYaw", stance.BladeYaw,
                new ConfigDescription("Where the blade points, degrees: 180 straight back, more turns it out to the left.", new AcceptableValueRange<float>(90f, 270f)));
            StanceBladePitch = _fixed.Bind("Stance", "BladePitch", stance.BladePitch,
                new ConfigDescription("How much the blade slopes down, degrees.", new AcceptableValueRange<float>(-30f, 80f)));
            StanceTwist = _fixed.Bind("Stance", "Twist", stance.SpineTwist,
                new ConfigDescription("Turn of the upper body, degrees (negative is to the left). The head keeps looking ahead.", new AcceptableValueRange<float>(-60f, 30f)));
            StanceLean = _fixed.Bind("Stance", "Lean", stance.SpineLean,
                new ConfigDescription("Forward lean of the upper body, degrees.", new AcceptableValueRange<float>(-20f, 40f)));
            StanceLeftHand = _fixed.Bind("Stance", "LeftHandOnBlade", stance.LeftAlongBlade,
                new ConfigDescription("Where the left hand holds the scabbard, meters along it from the right hand (the mouth is at about 0.12).", new AcceptableValueRange<float>(0.08f, 0.5f)));
            StanceSpineHold = _fixed.Bind("Stance", "SpineHold", 1f,
                new ConfigDescription("How much the stance holds the upper body upright over the game's idle, walk and run, which hunch it forward. 1 holds it fully, so the hands sit exactly where the stance puts them. 0 leaves the game's.", new AcceptableValueRange<float>(0f, 1f)));
            StanceTorsoHold = _fixed.Bind("Stance", "TorsoHold", 1f,
                new ConfigDescription("How much the stance keeps the chest facing ahead while the game's clips turn the hips: blocking while moving side to side, and the sway of each running stride. The legs keep their steps. 0 leaves the game's.", new AcceptableValueRange<float>(0f, 1f)));
            StanceShoulderHold = _fixed.Bind("Stance", "ShoulderHold", 1f,
                new ConfigDescription("How much the stance holds the collarbones still over the game's walk and run, which rock the shoulders under the hands on the grip. 0 leaves the game's.", new AcceptableValueRange<float>(0f, 1f)));
            // 0.2.0: Swing.CustomAnimations took over the two test switches, their old lines leave the config file
            DropOldSettings(("Swing", "FullBodyCuts"), ("Swing", "ClipTest"));
            DumpAnimationClips = _fixed.Bind("Debug", "DumpAnimationClips", false,
                "Write the names of all player animation clips and the playing states to the log once, for making replacement animations.");
            LogCounters = Config.Bind("Debug", "LogCounters", false,
                "Write every counter's damage and the learned swing timings to the log. For tuning.");
            RecordPoses = _fixed.Bind("Debug", "RecordPoses", false,
                "While a katana is in your hand, write your pose 120 times a second to BepInEx\\IaidoPoses (a new file each time it is taken out). For tuning the animations.");
            // every line no setting reads anymore goes too: the fine tuning values that left the file in 0.3.1 and older ones
            DropOldSettings();

            AddLocalization();
            ClipSwap.Load();
            PrefabManager.OnVanillaPrefabsAvailable += AddItems;
            new Harmony(ModGuid).PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo($"{ModName} {ModVersion} loaded");
        }

        // a setting still at an old default takes the new default, once: only when the file was last written by a version
        // older than the one that changed it (since), so a value picked back by hand stays
        private static void Renew(ConfigEntry<float> entry, float oldDefault, string since)
        {
            if (!ConfigMoves(_configFrom, since) || Mathf.Abs(entry.Value - oldDefault) >= 1e-4f) return;
            entry.Value = (float)entry.DefaultValue;
            Log.LogInfo($"config: {entry.Definition} moved from the old default {oldDefault} to {entry.Value}");
        }

        internal static bool ConfigMoves(System.Version from, string since) => from != null && from < new System.Version(since);

        // BepInEx starts the file with "## Settings file was created by plugin <name> v<version>". no file is a new
        // config with nothing to move, a file without a version counts as old
        internal static System.Version ConfigVersion(string path)
        {
            try
            {
                return File.Exists(path) ? ConfigVersionOf(File.ReadLines(path).FirstOrDefault()) : null;
            }
            catch (System.Exception)
            {
                return new System.Version(0, 0);
            }
        }

        internal static System.Version ConfigVersionOf(string firstLine)
        {
            var match = System.Text.RegularExpressions.Regex.Match(firstLine ?? "", @" v(\d+(\.\d+){1,3})\s*$");
            return match.Success ? new System.Version(match.Groups[1].Value) : new System.Version(0, 0);
        }

        // settings that no longer exist: BepInEx keeps their lines in the file as orphans, so they are dropped here. no
        // names drops every orphan, so call it that way only after the last setting is bound
        private void DropOldSettings(params (string section, string key)[] old)
        {
            try
            {
                if (!(AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(Config) is Dictionary<ConfigDefinition, string> orphans)) return;
                var gone = old.Length == 0 ? orphans.Keys.ToList() : old.Select(o => new ConfigDefinition(o.section, o.key)).ToList();
                int removed = 0;
                foreach (var definition in gone) if (orphans.Remove(definition)) removed++;
                if (removed == 0) return;
                Config.Save();
                Log.LogInfo($"config: {removed} old settings removed from the file");
            }
            catch (System.Exception e)
            {
                Log.LogWarning($"old settings stay in the config file: {e.Message}");
            }
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
            ParryClank.Prepare(shared, token);
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
            // the full-body cuts play over the base layer's greatsword swings, the upper body swings over the shovel's
            Moves.FullBody = CustomAnimations.Value && !UpperBodySwings.Value;
            Moves.OwnCuts = UpperBodySwings.Value || Moves.FullBody;
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
            ApplyAttackSettings(); // the hit areas follow the animations
        }

        // the katana's own animations (Moves.FullBody) get hit areas made for them, the borrowed ones keep krom's: every
        // swing passes through rocks, trees and objects, and the heavy, a cut from overhead to the ground, is a fan from
        // top to bottom (from 1 m up, 60 degrees above to 60 below level: over the head out in front, down to the ground
        // a little over half a meter ahead), a bit wider than the torso, the creature rays a little thicker
        private const float OwnHeavyRayWidth = 0.45f, OwnHeavyAngle = 120f, OwnHeavyCreatureExtra = 0.15f;
        private static readonly Dictionary<Attack, (bool Walls, float Low, float High, float Extra, Attack.AttackType Type)> KromHit =
            new Dictionary<Attack, (bool, float, float, float, Attack.AttackType)>();

        private static (bool Walls, float Low, float High, float Extra, Attack.AttackType Type) Krom(Attack attack)
        {
            if (!KromHit.TryGetValue(attack, out var krom))
                KromHit[attack] = krom = (attack.m_hitThroughWalls, attack.m_attackHeightChar1, attack.m_attackHeightChar2, attack.m_attackRayWidthCharExtra, attack.m_attackType);
            return krom;
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
                normal.m_attackStamina = flametal ? FlametalSwingStamina.Value : SwingStamina.Value;
                normal.m_attackRange = NormalRange.Value;
                normal.m_attackAngle = NormalAngle.Value;
                normal.m_attackRayWidth = NormalRayWidth.Value;
                heavy.m_attackRange = HeavyRange.Value;
                bool own = Moves.FullBody;
                var kromNormal = Krom(normal);
                var kromHeavy = Krom(heavy);
                normal.m_hitThroughWalls = own || kromNormal.Walls;
                heavy.m_hitThroughWalls = own || kromHeavy.Walls;
                heavy.m_attackType = own ? Attack.AttackType.Vertical : kromHeavy.Type;
                heavy.m_attackAngle = own ? OwnHeavyAngle : HeavyAngle.Value;
                heavy.m_attackRayWidth = own ? OwnHeavyRayWidth : HeavyRayWidth.Value;
                heavy.m_attackHeightChar1 = own ? 0f : kromHeavy.Low; // the fan itself runs from high to low
                heavy.m_attackHeightChar2 = own ? 0f : kromHeavy.High;
                heavy.m_attackRayWidthCharExtra = own ? OwnHeavyCreatureExtra : kromHeavy.Extra;
                normal.m_lowerDamagePerHit = false; // every swing cuts every enemy it reaches in full
                heavy.m_lowerDamagePerHit = false;
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
