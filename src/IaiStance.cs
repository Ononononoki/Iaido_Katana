using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace Iaido_Katana
{
    // the iai stance: while a player blocks with a katana, the upper body turns left and the arms hold the sword at
    // the left hip, edge up, like a sword in its scabbard. it is laid over the game's own block animation after the
    // animator, for every player in view, so friends see it too. only bones are turned, nothing is sent
    internal static class IaiStance
    {
        private static readonly AccessTools.FieldRef<Character, Animator> AnimatorOf = AccessTools.FieldRefAccess<Character, Animator>("m_animator");
        private static readonly AccessTools.FieldRef<Humanoid, VisEquipment> VisOf = AccessTools.FieldRefAccess<Humanoid, VisEquipment>("m_visEquipment");
        private static readonly AccessTools.FieldRef<VisEquipment, int> RightItem = AccessTools.FieldRefAccess<VisEquipment, int>("m_currentRightItemHash");
        private static readonly AccessTools.FieldRef<Character, ZNetView> NViewOf = AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<VisEquipment, int> BackItem = AccessTools.FieldRefAccess<VisEquipment, int>("m_currentRightBackItemHash");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> BackInstance = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightBackItemInstance");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> HandInstance = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightItemInstance");
        // the game's collarbone move for the female body (CharacterAnimEvent.CustomLateUpdate), see ShoulderPlaces
        private static readonly AccessTools.FieldRef<Character, CharacterAnimEvent> AnimEventOf = AccessTools.FieldRefAccess<Character, CharacterAnimEvent>("m_animEvent");
        private static readonly AccessTools.FieldRef<CharacterAnimEvent, bool> FemaleHack = AccessTools.FieldRefAccess<CharacterAnimEvent, bool>("m_femaleHack");
        private static readonly AccessTools.FieldRef<CharacterAnimEvent, Transform> LeftShoulderOf = AccessTools.FieldRefAccess<CharacterAnimEvent, Transform>("m_leftShoulder");
        private static readonly AccessTools.FieldRef<CharacterAnimEvent, Transform> RightShoulderOf = AccessTools.FieldRefAccess<CharacterAnimEvent, Transform>("m_rightShoulder");
        private static readonly AccessTools.FieldRef<CharacterAnimEvent, float> FemaleOffset = AccessTools.FieldRefAccess<CharacterAnimEvent, float>("m_femaleOffset");
        private static readonly AccessTools.FieldRef<CharacterAnimEvent, float> MaleOffset = AccessTools.FieldRefAccess<CharacterAnimEvent, float>("m_maleOffset");
        private static readonly AccessTools.FieldRef<CharacterAnimEvent, VisEquipment> AnimEventVis = AccessTools.FieldRefAccess<CharacterAnimEvent, VisEquipment>("m_visEquipment");
        // the cut's trail runs from the draw to the end of the cut, then fades out in its colours
        private const float TrailFrom = 0.04f, TrailTo = 0.2f;
        // counts the slashes of a player in its network data, so every client sees each one
        private static readonly int SlashCount = "IaidoKatana_slash".GetStableHashCode();
        // the same for the flametal thunderclap: how many were started, the last one called off, and its gathering time
        private static readonly int ThunderCount = "IaidoKatana_thunder".GetStableHashCode();
        private static readonly int ThunderStop = "IaidoKatana_thunder_stop".GetStableHashCode();
        private static readonly int ThunderPrepKey = "IaidoKatana_thunder_prep".GetStableHashCode();
        private static readonly int HeavyCount = "IaidoKatana_heavy".GetStableHashCode();
        private static readonly CrouchSettings Crouch = new CrouchSettings();
        private static readonly int SteelHash = Katana.SteelPrefab.GetStableHashCode();
        private static readonly int FlametalHash = Katana.FlametalPrefab.GetStableHashCode();

        // spine first, then the arms, the order the pose is built in. the hips and legs only move in the thunderclap's crouch
        // and under the katana's own cuts
        private static readonly string[] BoneNames = { "Spine1", "Spine2", "Neck", "RightArm", "RightForeArm", "RightHand", "LeftArm", "LeftForeArm", "LeftHand",
                                                       "Hips", "LeftUpLeg", "LeftLeg", "LeftFoot", "RightUpLeg", "RightLeg", "RightFoot", "Spine",
                                                       "RightShoulder", "LeftShoulder" };
        private const int Spine1 = 0, Spine2 = 1, Neck = 2, RArm = 3, RFore = 4, RHand = 5, LArm = 6, LFore = 7, LHand = 8;
        private const int HipsBone = 9, LUp = 10, LLow = 11, LFoot = 12, RUp = 13, RLow = 14, RFoot = 15, SpineLow = 16;
        private const int RClavicle = 17, LClavicle = 18;

        // the hips and legs of the ripped player at rest. the katana's own cuts stand on their own footing built from
        // these, faded in over the animator's (BodyIn) and back out after (BodyOut)
        private static readonly Quaternion RestHipsRot = new Quaternion(0.7071068f, 0f, 0f, 0.7071068f);
        private static readonly Vector3 RestHipsLocal = new Vector3(0f, 7.026316e-05f, 0.009579787f);
        private static readonly Quaternion[] RestLegs =
        {
            new Quaternion(0.00026014916f, -0.03361914f, 0.9994042f, 0.0078065796f), new Quaternion(-0.0042211507f, -2.2611024e-05f, 0.003433189f, 0.9999852f),
            new Quaternion(0.48492208f, -0.03238013f, 0.017970623f, 0.873773f),
            new Quaternion(-0.00026014916f, -0.03361914f, 0.9994042f, -0.0078065796f), new Quaternion(-0.0038573912f, 2.066251e-05f, -0.0031373324f, 0.99998766f),
            new Quaternion(0.48492208f, 0.03238013f, -0.017970623f, 0.873773f),
        };
        private const float BodyIn = 0.12f, BodyOut = 0.25f;
        private const float SettleSeconds = 0.2f; // the sheathed blade and its scabbard settling into the stance
        private const float FollowEase = 0.1f;    // the scabbard taking up the sliding blade
        internal const float ElbowTurnSpeed = 900f; // degrees a second an elbow may swing round the arm's line
        internal const float FullElbowTurnSpeed = 2500f; // in a full-body cut's 80 ms strike the whole arm whips round
        private static readonly CrouchSettings Footing = new CrouchSettings();
        private static readonly Quaternion[] KeptBody = new Quaternion[RFoot - HipsBone + 1];

        // the spine of the ripped player at rest. the katana's own cuts start from it, so the greatsword swing playing
        // under them can not twist the upper body away from the cut
        private static readonly Quaternion RestSpine = new Quaternion(-0.03922647f, 0f, 0f, 0.9992304f);
        private static readonly Quaternion RestSpine1 = new Quaternion(-0.010170685f, 0f, 0f, 0.9999483f);
        private static readonly Quaternion RestSpine2 = new Quaternion(0f, 0f, 0f, 1f);
        private static readonly Quaternion RestNeck = new Quaternion(0.34641975f, -0.008183268f, 0.026077881f, 0.9376814f);

        // the collarbones at rest (the rip's rig). the walk and run rock them under the hands on the grip (ShoulderHold)
        private static readonly Quaternion RestRClavicle = new Quaternion(-0.56428975f, -0.41886535f, 0.5860074f, -0.40339094f);
        private static readonly Quaternion RestLClavicle = new Quaternion(0.56428975f, -0.41886535f, 0.5860074f, 0.40339094f);

        // blocking, the hips are turned back toward the block idle's turn by this share too, at most HipsHoldMax degrees,
        // each leg put back onto its foot (Drive): with only the chest held, the hips and legs swung 40 degrees alone under
        // it from side to side (0.2.1, the user: "janky in the legs"). Unity on the game's clips: the chest stays at the
        // block idle's turn strafing both ways, twisted against the hips at most 55 instead of 61
        internal const float BlockHipsYawDefault = 51.8f; // the hips' turn in the game's block idle, on the bone
        internal const float HipsHoldShare = 0.7f, HipsHoldMax = 40f;
        internal const float BlockHipsBlend = 0.15f; // seconds the block's hips hold takes to come in and go again
        // degrees the left collarbone is lifted about the body's forward line in the stance: the shoulder about 1.7 cm
        // higher, the left elbow about 2 cm nearer the body in the idle, the walk and the block
        internal const float LeftShoulderLift = 7f;

        private sealed class State
        {
            internal Transform[] Bones;
            internal Transform Hips, Attach;
            internal readonly Quaternion[] Animated = new Quaternion[BoneNames.Length];
            internal readonly Quaternion[] Written = new Quaternion[BoneNames.Length];
            internal bool HasWritten;
            internal float Weight;
            internal bool Broken;
            internal float SlashStart = -1f;
            internal int SeenSlashes = -1;
            internal float HoldUntil = -1f;  // the slash stands still until then (the counter's hit stop)
            internal float HoldAt;
            internal float SettleStart = -1f;  // the sheathing ended: the sheathed blade settles into the stance from SettleFrom
            internal bool SayaFollows;         // while the blade slides in, the scabbard is where the blade really is, slid back in
            internal float FollowStart = -1f;
            internal Vector3 FollowPos;
            internal Quaternion FollowRot;
            internal GameObject PutAwayLightning; // the flametal lightning on the katana put away at the hip
            internal Moves.Pose SettleFrom;
            internal int Move;                 // the katana's own moves: 0 none, 1 a cut of the combo, 2 the flick and sheathing
            internal int MoveStep;
            internal bool MoveFromScabbard;
            internal float MoveStart, MoveClipStart, MoveHold;
            internal bool MoveClicked;
            internal Moves.Pose MoveFrom;      // where the move started from
            internal Moves.Pose LastPose;      // what was shown last frame, when it was fully ours
            internal bool HasLast;
            internal bool LastSheathed;        // and whether the blade was in its scabbard then
            internal bool WasAttacking;
            internal bool HidePending;         // put away was pressed while the blade was out: after the sheathing
            internal GameObject Saya;          // the scabbard at the hip, and the katana it was made for
            internal int SayaItem;
            internal Vector3 SayaMouth;        // the middle of its mouth, in the scabbard's frame
            internal GameObject Lightning;     // the flametal blade's lightning on the katana in the hand
            internal Vector3 SheathPos;        // where the blade really is in the full stance, from the hips and facing
            internal Quaternion SheathRot;
            internal bool HasSheath;
            internal Transform Blade;          // the katana mesh in the hand
            internal Vector3 HipPos;           // the same place from the hips bone, for the katana put away
            internal Quaternion HipRot;
            internal bool HasHip;
            internal GameObject Sheathed;      // the katana lying in the scabbard while it is put away
            internal GameObject HiddenBack;    // the game's katana on the back, hidden while the scabbard holds it
            internal ParticleSystem Sparks;
            internal bool InSlash;
            internal bool TrailOn;             // the cut's trail is being drawn
            internal BladeTrail Trail;
            internal float Body;               // how much the hips and legs stand on the cut's own footing
            internal float ThunderStart = -1f; // the flametal thunderclap: when it started here, -1 when none
            internal float ThunderPrep;
            internal int SeenThunder = -1;
            internal int ThunderStage;         // 0 gathering, 1 cutting, 2 holding and sheathing, 3 sheathed
            internal ThunderCharge Charge;     // the gathering's sound and ball of lightning, while it plays
            internal bool ArcShown;            // this parry's afterimage was made
            internal float HeavyLast = -1f;    // the steel heavy's clip time last frame (a smaller one is a new heavy)
            internal bool HeavySwell, HeavyClimb, HeavyShone, HeavySwung, HeavyArc; // its swell, glint, shine, swing sound and afterimage
            internal float HeavySwellAt, HeavySwellPeak; // when the swell began and how many seconds later it was set to peak
            internal Transform HeavyGlint;     // the glint climbing the blade
            internal GameObject HeavyShine;    // the shine at the tip, gone when the blade starts to fall
            internal Vector3 ThunderFrom;      // where the thunderclap's dash began, as this client saw it
            internal bool ThunderTrailShown;   // its afterimage was made
            internal Vector3 AnimatedHips;     // the hips' place from the animator, and what the crouch wrote
            internal Vector3 WrittenHips;
            internal readonly StanceSolver.ElbowMemory Elbows = new StanceSolver.ElbowMemory(); // the elbows turn smoothly
            internal bool RollSheathed;        // the katana rests in the scabbard through a roll, or while it is taken out
            internal bool HadKatana;           // a katana was in the hand last frame
            internal float TakeOutAt = -1f;    // when the put away katana was taken out again
            internal float HeavyAt = -9f;      // when this player's last heavy attack began (its clip is the combo's third)
            internal int SeenHeavy = -1;
            internal GameObject HiddenHand;    // the katana in the hand, hidden meanwhile
            internal float SpineLogAt;         // the next debug line of the animator's spine
            internal readonly FootPlant Feet = new FootPlant(); // a full-body cut's feet on the ground
            internal readonly CrouchSettings LastFooting = new CrouchSettings(); // what it showed last, held while the body fades out
            internal bool HasLastFooting;
            internal bool MoveBlend;           // a full-body cut that began from the game's pose eases in from it
            internal bool SheatheBlend;        // the sheathing after one of the katana's own clips eases in from its pose
            internal bool StrikeSounded;       // its whoosh played
            internal bool FadeCut;             // a full-body cut handed over to one of the game's swings fades out from FadePose
            internal Moves.Pose FadePose;
            internal float FadeHead, LastHead; // and the head's look down, held with it
            internal bool MarkPending;         // a landed full-body cut leaves its slash mark once the pose is written
            internal bool OwnClip;             // one of the katana's own animation clips is playing (Swing.CustomAnimations)
            internal int OwnStep;              // the combo step (or the heavy) it plays
            internal bool WasOwnClip;          // and was last frame, at this clip time
            internal float LastOwnTime;
            internal System.IO.StreamWriter RecPose, RecGame, RecFrames; // Debug.RecordPoses, open while a katana is held
            internal float RecStart, RecNext;
            internal int RecCount;
            internal bool RecBroken;
            internal Transform Head;
            internal bool OwnBlending;         // the animator is still blending into that clip
            internal bool HeldIntoClip;        // the stance was held through that blend (logged once)
            internal float BlockHipsYaw = BlockHipsYawDefault; // and the hips' turn there
            internal float BlockHipsWeight;   // how far the block's hips hold is in (eased over BlockHipsBlend)
        }

        private const float FullBlendIn = 0.08f; // seconds a full-body cut takes to take over from the game's pose
        // seconds the stance takes to hand over to one of the katana's own clips, and to take over again after one
        // (0.2.1, the user's test: the 0.06 s blend out and the sheathing's one-frame take over looked like teleports)
        private const float ClipHandover = 0.15f;
        // put away (R): the hand lets go of the handle over this while the game's reach to the hip plays, and taken out
        // again the katana stays in its scabbard until the stance has the hand on the grip, at most TakeOutMax seconds
        // (0.2.1, the user: putting away and taking out were instant and looked broken)
        private const float PutAwayBlend = 0.25f;
        private const float TakeOutMax = 0.8f;
        private const float FullFadeOut = 0.1f;  // and to hand over to the game's next swing

        // the local player started a heavy attack on the upper body layer, whose clip is also the combo's third cut:
        // mark it here and tell the other clients
        internal static void StartHeavy(Player player)
        {
            var s = StateOf(player);
            s.HeavyAt = Time.time;
            var zdo = NViewOf(player)?.GetZDO();
            if (zdo == null) return;
            int count = zdo.GetInt(HeavyCount, 0) + 1;
            zdo.Set(HeavyCount, count, false);
            s.SeenHeavy = count;
        }

        // another client's player started a heavy attack
        private static void WatchHeavy(Player player, State s)
        {
            var nview = NViewOf(player);
            if (!nview || !nview.IsValid() || nview.IsOwner()) return;
            int count = nview.GetZDO().GetInt(HeavyCount, 0);
            if (s.SeenHeavy >= 0 && count != s.SeenHeavy) s.HeavyAt = Time.time;
            s.SeenHeavy = count;
        }

        private const float HeavyWindow = 0.6f; // the heavy's clip shows up this soon after it began

        // the local player started the thunderclap: pose it here and tell the other clients
        internal static void StartThunder(Player player, float prep)
        {
            var s = StateOf(player);
            s.ThunderStart = Time.time;
            s.ThunderPrep = prep;
            s.ThunderStage = 0;
            StartCharge(player, s);
            var zdo = NViewOf(player)?.GetZDO();
            if (zdo == null) return;
            int count = zdo.GetInt(ThunderCount, 0) + 1;
            zdo.Set(ThunderPrepKey, prep);
            zdo.Set(ThunderCount, count, false);
            s.SeenThunder = count;
        }

        // the local player's thunderclap was called off
        internal static void StopThunder(Player player)
        {
            if (States.TryGetValue(player, out var s)) EndThunder(player, s);
            var zdo = NViewOf(player)?.GetZDO();
            if (zdo != null) zdo.Set(ThunderStop, zdo.GetInt(ThunderCount, 0), false);
        }

        private static void EndThunder(Player player, State s)
        {
            s.ThunderStart = -1f;
            if (s.Charge) s.Charge.Cancel(); // called off before the cut: it fades out, after the cut it plays on
            s.Charge = null;
        }

        // the gathering's sound and ball of lightning at the hands, for this player on this client
        private static void StartCharge(Player player, State s)
        {
            if (s.Charge) s.Charge.Cancel();
            var bones = s.Bones;
            s.Charge = ThunderCharge.Begin(player, bones != null ? bones[RHand] : null, bones != null ? bones[LHand] : null, s.ThunderPrep);
        }

        // another client's player started or called off a thunderclap
        private static void WatchThunder(Player player, State s)
        {
            var nview = NViewOf(player);
            if (!nview || !nview.IsValid() || nview.IsOwner()) return;
            var zdo = nview.GetZDO();
            int count = zdo.GetInt(ThunderCount, 0);
            if (s.SeenThunder >= 0 && count != s.SeenThunder)
            {
                s.ThunderStart = Time.time;
                s.ThunderPrep = zdo.GetFloat(ThunderPrepKey, Plugin.ThunderPrep.Value);
                s.ThunderStage = 0;
                StartCharge(player, s);
            }
            s.SeenThunder = count;
            if (s.ThunderStart >= 0f && zdo.GetInt(ThunderStop, 0) == count) EndThunder(player, s);
        }

        private const float RemoteTrailDelay = 0.2f; // another client's player reaches the dash's end this much later here

        // the steel heavy on every client, by its clip's time (the two heavy clips share these keys): the swell rises from
        // the start and peaks as the swing sound starts, a gold glint climbs the blade from the guard to the tip with it,
        // slow and faint first, then faster and brighter like the swell, and shines at the tip until the blade starts to
        // fall, the swing sound is loudest on the hit, and an upright golden afterimage stays where it struck
        // the climb takes ClimbShare of the swell's rise (climb = share squared), the shine stays where the tip was and is
        // gone when the strike begins (the raise's end with the swing warp) or at the latest when the cut comes over the
        // top (FallAt)
        private const float GlintFrom = 0.45f, FallAt = 0.896f, ClimbFrom = 0.3f, ClimbTo = 1.8f, ClimbShare = 0.85f, ClimbFaint = 0.15f;
        private const float WarpedRaise = 0.95f, SwellWait = 0.1f; // the warp's raise plays below this speed (0.58 by default)
        private const float SwellHeight = 1.2f; // the swell sounds from the body's middle, not the hand at the side

        // real seconds from this clip time until the clip reaches target at the speed now. with the swing warp (the
        // animator speed is synced, so every client plays it) the raise is slow up to its end and the strike after it
        // takes almost no time, so anything past the raise's end is reached at about the raise's end
        private static float HeavyUntil(float clipTime, float speed, float target)
        {
            float end = Plugin.SwingTimeWarp.Value ? Mathf.Min(target, SwingWarp.HeavyRaiseEnd) : target;
            return Mathf.Max(0f, end - clipTime) / speed;
        }

        private static void HeavyEvents(Player player, State s, float clipTime)
        {
            if (clipTime < s.HeavyLast) EndHeavy(s); // a new heavy began
            s.HeavyLast = clipTime;
            if (!s.Attach) return;
            var animator = AnimatorOf(player);
            float speed = animator && animator.speed > 0.05f ? animator.speed : 1f;
            var blade = s.Attach.rotation * Vector3.forward;
            bool flametal = RightItem(VisOf(player)) == FlametalHash;
            float hit = Moves.HitOf(s.OwnStep);
            // the swell waits for the warp's slow raise speed to arrive (a frame or two, longer for another client's
            // player) so its timing is right, at most until SwellWait into the clip
            if (!s.HeavySwell && clipTime < GlintFrom && (!Plugin.SwingTimeWarp.Value || speed < WarpedRaise || clipTime >= SwellWait))
            {
                s.HeavySwell = true;
                s.HeavySwellAt = Time.time;
                s.HeavySwellPeak = HeavyUntil(clipTime, speed, hit - HeavySound.SwingPeak * speed);
                HeavySound.Swell(player.transform.position + Vector3.up * SwellHeight, s.HeavySwellPeak);
                if (Plugin.LogCounters.Value) Plugin.Log.LogInfo($"steel heavy: swell from clip {clipTime:0.00} s at speed {speed:0.00}, peaking in {s.HeavySwellPeak:0.00} s");
            }
            float share = s.HeavySwell ? Mathf.Clamp01((Time.time - s.HeavySwellAt) / Mathf.Max(0.05f, s.HeavySwellPeak * ClimbShare)) : 0f;
            if (!s.HeavyClimb && s.HeavySwell && share < 1f)
            {
                s.HeavyClimb = true;
                s.HeavyGlint = KatanaFx.ClimbingGlint();
                if (Plugin.LogCounters.Value) Plugin.Log.LogInfo($"steel heavy: glint climbing for {s.HeavySwellPeak * ClimbShare:0.00} s with the swell");
            }
            if (s.HeavyGlint && share < 1f)
            {
                float climb = share * share; // slow first, faster to the end
                KatanaFx.MoveGlint(s.HeavyGlint, s.Attach.position + blade * Mathf.Lerp(ClimbFrom, ClimbTo, climb), Mathf.Lerp(ClimbFaint, 1f, climb));
            }
            if (!s.HeavyShone && s.HeavySwell && share >= 1f)
            {
                s.HeavyShone = true;
                if (s.HeavyGlint) Object.Destroy(s.HeavyGlint.gameObject);
                s.HeavyGlint = null;
                var tip = s.Attach.position + blade * ClimbTo;
                float lasts = Mathf.Max(0.05f, HeavyUntil(clipTime, speed, FallAt));
                s.HeavyShine = KatanaFx.Glint(tip, true, lasts);
                if (Plugin.LogCounters.Value) Plugin.Log.LogInfo($"steel heavy: shine at the tip for {lasts:0.00} s, {tip.y - player.transform.position.y:0.00} m up, {Vector3.Distance(tip, player.transform.position):0.00} m from the feet");
            }
            if (s.HeavyShine && clipTime >= FallAt)
            {
                Object.Destroy(s.HeavyShine);
                s.HeavyShine = null;
            }
            if (!s.HeavySwung && clipTime >= hit - HeavySound.SwingPeak * speed)
            {
                s.HeavySwung = true;
                HeavySound.Swing(s.Attach.position);
                if (Plugin.LogCounters.Value && s.HeavySwell)
                    Plugin.Log.LogInfo($"steel heavy: swing sound {Time.time - s.HeavySwellAt:0.00} s after the swell began (its peak set for {s.HeavySwellPeak:0.00} s)");
            }
            if (!s.HeavyArc && clipTime >= hit)
            {
                s.HeavyArc = true;
                KatanaFx.SlashArc(player.transform, flametal, false, true);
                if (Plugin.LogCounters.Value) Plugin.Log.LogInfo($"steel heavy: struck, upright afterimage for {player.GetPlayerName()}");
            }
        }

        private static void EndHeavy(State s)
        {
            if (s.HeavyGlint) Object.Destroy(s.HeavyGlint.gameObject);
            s.HeavyGlint = null;
            if (s.HeavyShine) Object.Destroy(s.HeavyShine);
            s.HeavyShine = null;
            s.HeavySwell = s.HeavyClimb = s.HeavyShone = s.HeavySwung = s.HeavyArc = false;
            s.HeavyLast = -1f;
        }

        // the glint as the blade leaves, the afterimage when the dash ends, and the click when it is back in its scabbard
        private static void ThunderEvents(Player player, State s, float t)
        {
            float prep = s.ThunderPrep;
            if (s.ThunderStage == 0 && t >= prep)
            {
                s.ThunderStage = 1;
                KatanaFx.Glint(Mouth(s));
                s.ThunderFrom = player.transform.position;
                s.ThunderTrailShown = false;
            }
            // the dash is over (a little later for another client's player, whose position arrives late): its afterimage
            var nview = NViewOf(player);
            float trailAt = prep + Thunderclap.DashSeconds + (nview && nview.IsOwner() ? 0f : RemoteTrailDelay);
            if (s.ThunderStage >= 1 && !s.ThunderTrailShown && t >= trailAt)
            {
                s.ThunderTrailShown = true;
                string kind = Plugin.ThunderAfterimage.Value;
                if (kind == "Path" || kind == "Both") KatanaFx.SlashPath(s.ThunderFrom, player.transform.position);
                if (kind == "Arc" || kind == "Both") KatanaFx.SlashArc(player.transform, true, false);
                if (Plugin.LogCounters.Value && kind != "Off")
                    Plugin.Log.LogInfo($"thunderclap afterimage ({kind}) for {player.GetPlayerName()}, {Vector3.Distance(s.ThunderFrom, player.transform.position):0.0} m");
            }
            if (s.ThunderStage == 1 && t >= prep + Thunder.Cut) s.ThunderStage = 2;
            if (s.ThunderStage == 2 && t >= Thunder.SheathedAt(prep))
            {
                s.ThunderStage = 3;
                ImpactFrame.Click(Mouth(s));
            }
        }

        // a katana the player put away, which the game would hang on the back
        private static int PutAway(Player player)
        {
            var vis = VisOf(player);
            if (!vis) return 0;
            int item = BackItem(vis);
            return item == SteelHash || item == FlametalHash ? item : 0;
        }

        private static void HideSaya(State s)
        {
            if (s.Saya) s.Saya.SetActive(false);
            RestoreBack(s);
            if (s.HiddenHand) s.HiddenHand.SetActive(true);
            s.HiddenHand = null;
            s.RollSheathed = false;
        }

        // the katana in the hand hidden while it rests in the scabbard through a roll, and shown again after
        private static void HideHand(Player player, State s, bool hide)
        {
            var hand = hide ? HandInstance(VisOf(player)) : null;
            if (hand)
            {
                if (hand.activeSelf) hand.SetActive(false);
                s.HiddenHand = hand;
                return;
            }
            if (s.HiddenHand) s.HiddenHand.SetActive(true);
            s.HiddenHand = null;
        }

        // the game's tag on its dodge states, hashed on first use (unity's hash only runs inside the game)
        private static int _dodgeTag;
        private static int DodgeTag => _dodgeTag != 0 ? _dodgeTag : _dodgeTag = Animator.StringToHash("dodge");

        // the player is rolling: the animator plays the dodge. every client sees this, unlike Player.InDodge, which
        // only the player's own client knows
        private static bool Rolling(Player player)
        {
            var animator = AnimatorOf(player);
            if (!animator) return false;
            return animator.GetCurrentAnimatorStateInfo(0).tagHash == DodgeTag
                   || animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).tagHash == DodgeTag;
        }

        // with Debug.DumpAnimationClips on: the animator's spine for the local player every 2 s, as turns from rest
        // (degrees about x, y, z) and the hips' height, to fit the offline checks to the game's idle, walk and run
        private static void LogSpine(Player player, State s)
        {
            s.SpineLogAt = Time.time + 2f;
            string Turn(Quaternion rest, Quaternion animated)
            {
                var e = (Quaternion.Inverse(rest) * animated).eulerAngles;
                float W(float a) => a > 180f ? a - 360f : a;
                return $"({W(e.x):0}, {W(e.y):0}, {W(e.z):0})";
            }
            var animator = AnimatorOf(player);
            animator.GetCurrentAnimatorClipInfo(0, ClipBuffer);
            string clips = string.Join("+", ClipBuffer.Where(c => c.clip).Select(c => c.clip.name));
            Plugin.Log.LogInfo($"spine: Spine {Turn(RestSpine, s.Animated[SpineLow])} Spine1 {Turn(RestSpine1, s.Animated[Spine1])} Spine2 {Turn(RestSpine2, s.Animated[Spine2])}"
                               + $" Neck {Turn(RestNeck, s.Animated[Neck])} hips {s.AnimatedHips.y * 100f:0.00}"
                               + $" hips turn {StanceSolver.Yaw(player.transform.rotation, QMath.Mul(s.Bones[HipsBone].parent.rotation, s.Animated[HipsBone])):0}"
                               + $" | {clips}");
        }

        private static void RestoreBack(State s)
        {
            if (s.HiddenBack) s.HiddenBack.SetActive(true);
            s.HiddenBack = null;
        }

        // the katana mesh in the hand. the game puts the item in the hand at its own size, not the hand's (the player
        // is scaled 0.95), so the scabbard is measured from this and not from the hand
        private static Transform BladeOf(State s)
        {
            if (s.Blade && s.Blade.IsChildOf(s.Attach)) return s.Blade;
            s.Blade = null;
            foreach (var filter in s.Attach.GetComponentsInChildren<MeshFilter>())
                if (filter.name == "katana") s.Blade = filter.transform;
            return s.Blade;
        }

        private static readonly StanceSettings SayaSettings = new StanceSettings();

        // the scabbard hangs at the left hip exactly where the stance holds the blade, so in the stance the blade lies
        // inside it, and the draw pulls the blade out of it. shown while a katana is in the hand, and with the katana in
        // it while the katana is put away, on every player in view
        private static void UpdateSaya(Player player, State s, bool katana, int back)
        {
            bool holster = !katana && back != 0 && Plugin.HolsterOn.Value;
            bool rolled = katana && s.RollSheathed && s.HasHip;
            if (!(katana || holster) || !Plugin.StanceEnabled.Value || !Plugin.ScabbardOn.Value || player.IsDead())
            {
                HideSaya(s);
                return;
            }
            var parent = AnimatorOf(player).transform;
            int item = katana ? RightItem(VisOf(player)) : back;
            if (!s.Saya || s.SayaItem != item)
            {
                string look = item == FlametalHash ? "flametal" : "steel";
                var mesh = KatanaModel.SayaMeshFor(look);
                var material = KatanaModel.MaterialFor(look);
                if (!mesh || !material) return;
                if (!s.Saya)
                {
                    s.Saya = new GameObject("IaidoKatana_saya", typeof(MeshFilter), typeof(MeshRenderer));
                    s.Saya.transform.SetParent(parent, false);
                    s.Saya.layer = parent.gameObject.layer;
                    // the katana itself, in the scabbard's frame, shown while it is put away
                    s.Sheathed = new GameObject("IaidoKatana_sheathed", typeof(MeshFilter), typeof(MeshRenderer));
                    s.Sheathed.transform.SetParent(s.Saya.transform, false);
                    s.Sheathed.layer = s.Saya.layer;
                }
                s.Sheathed.GetComponent<MeshFilter>().sharedMesh = KatanaModel.KatanaMeshFor(look);
                if (s.PutAwayLightning) Object.Destroy(s.PutAwayLightning);
                s.PutAwayLightning = look == "flametal" ? KatanaFx.CopyLightning(Katana.FlametalPrefab, s.Sheathed.transform) : null;
                s.SayaMouth = KatanaModel.SayaMouthFor(look);
                s.Saya.GetComponent<MeshFilter>().sharedMesh = mesh;
                s.Saya.GetComponent<MeshRenderer>().sharedMaterial = material;
                s.Sheathed.GetComponent<MeshRenderer>().sharedMaterial = material;
                if (item == FlametalHash && !s.Sparks && Plugin.SparksOn.Value) s.Sparks = KatanaFx.Sparks(s.Saya.transform, s.SayaMouth);
                s.SayaItem = item;
            }
            s.Saya.SetActive(true);
            s.Sheathed.SetActive(holster || rolled);

            // put away: the game's katana on the back is hidden, this one lies in the scabbard
            var putAway = holster ? BackInstance(VisOf(player)) : null;
            if (putAway)
            {
                if (putAway.activeSelf) putAway.SetActive(false);
                s.HiddenBack = putAway;
            }
            else RestoreBack(s);

            // lightning at the flametal scabbard's mouth while the blade sits in it in the stance, building up while the
            // thunderclap gathers
            float crackle = 0f;
            if (Plugin.SparksOn.Value && item == FlametalHash && katana)
            {
                float gathered = s.ThunderStart >= 0f ? (Time.time - s.ThunderStart) / Mathf.Max(0.1f, s.ThunderPrep) : -1f;
                if (gathered >= 0f) crackle = gathered < 1f ? 1f + 5f * gathered : 0f;
                else if (s.Weight >= 0.9f && !s.InSlash && s.Move == 0) crackle = 1f;
            }
            KatanaFx.SparkRate(s.Sparks, crackle);

            // where the blade really lies in the full stance (the arm may fall a little short of the stance's aim), so
            // the blade always ends up inside. before the first stance, the stance's aim. put away, the same place
            // follows the hips bone, so it moves with the hips when swimming, sitting or lying down
            var root = player.transform.rotation;
            var origin = s.Hips.position - QMath.Rotate(root, StanceSolver.RestHips);
            Vector3 grip;
            Quaternion attach;
            if ((holster || rolled) && s.HasHip)
            {
                grip = s.Hips.position + QMath.Rotate(s.Hips.rotation, s.HipPos);
                attach = QMath.Mul(s.Hips.rotation, s.HipRot);
            }
            else if (s.HasSheath)
            {
                grip = origin + QMath.Rotate(root, s.SayaFollows ? s.FollowPos : s.SheathPos);
                attach = QMath.Mul(root, s.SayaFollows ? s.FollowRot : s.SheathRot);
            }
            else
            {
                ReadSettings(SayaSettings);
                StanceSolver.Sword(origin, root, SayaSettings, out grip, out attach, out _);
            }
            if (holster && !s.HasHip && Upright(player)) LearnHip(s, grip, attach);
            // the scabbard shares the katana mesh's frame, so it goes exactly where the mesh sits from the hand, at its size
            var offset = KatanaModel.NodePosition;
            var turn = KatanaModel.NodeRotation;
            float scale = 1f;
            var blade = BladeOf(s);
            if (blade)
            {
                var hand = QMath.Conj(s.Attach.rotation);
                offset = QMath.Rotate(hand, blade.position - s.Attach.position);
                turn = QMath.Mul(hand, blade.rotation);
                scale = blade.lossyScale.x;
            }
            s.Saya.transform.position = grip + QMath.Rotate(attach, offset);
            s.Saya.transform.rotation = QMath.Mul(attach, turn);
            s.Saya.transform.localScale = Vector3.one * (scale / Mathf.Max(1e-4f, parent.lossyScale.x));
        }

        // standing or walking on the ground, so the hips are as they are in the stance
        private static bool Upright(Player player) =>
            player.IsOnGround() && !player.IsSwimming() && !player.IsAttached() && !player.InBed() && !player.IsSitting();

        // the blade's place from the hips bone, for the katana put away
        private static void LearnHip(State s, Vector3 grip, Quaternion attach)
        {
            var hips = QMath.Conj(s.Hips.rotation);
            s.HipPos = QMath.Rotate(hips, grip - s.Hips.position);
            s.HipRot = QMath.Mul(hips, attach);
            s.HasHip = true;
        }

        // the middle of the scabbard's mouth in the world, where the glint and the click come from
        private static Vector3 Mouth(State s) =>
            s.Saya && s.Saya.activeInHierarchy ? s.Saya.transform.TransformPoint(s.SayaMouth) : s.Attach.position;

        // the parry's cut began: a glint at the scabbard mouth
        private static void SlashBegins(State s) => KatanaFx.Glint(Mouth(s));

        // which swing clip is playing or blending in: a cut of the combo (step 0 to 2, and how far into its clip, in the
        // time its keys are written in), the heavy one, or another attack. the upper body layer's swings (Swing.UpperBody)
        // first, then the base layer's greatsword and sledge swings
        private static bool AttackClip(Animator animator, State s, out int step, out float clipTime, out bool other)
        {
            step = -1;
            clipTime = 0f;
            other = false;
            s.OwnClip = false;
            s.OwnBlending = false;
            if (!animator) return false;
            if (animator.layerCount > 1)
            {
                bool nextUp = animator.IsInTransition(1);
                var up = nextUp ? animator.GetNextAnimatorStateInfo(1) : animator.GetCurrentAnimatorStateInfo(1);
                if (nextUp) animator.GetNextAnimatorClipInfo(1, ClipBuffer);
                else animator.GetCurrentAnimatorClipInfo(1, ClipBuffer);
                foreach (var clip in ClipBuffer)
                {
                    if (!clip.clip) continue;
                    int i = System.Array.IndexOf(Moves.UpperClips, clip.clip.name);
                    if (i < 0) continue;
                    // the heavy plays the combo's third clip: it is the heavy when one just began, or still going
                    bool heavy = i == 2 && (s.Move == 1 && s.MoveStep == Moves.HeavyStep || Time.time - s.HeavyAt < HeavyWindow);
                    step = heavy ? Moves.HeavyStep : i;
                    clipTime = Moves.KeyTime(step, up.normalizedTime * clip.clip.length);
                    return true;
                }
            }
            bool next = animator.IsInTransition(0);
            var info = next ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
            if (next) animator.GetNextAnimatorClipInfo(0, ClipBuffer);
            else animator.GetCurrentAnimatorClipInfo(0, ClipBuffer);
            foreach (var clip in ClipBuffer)
            {
                if (!clip.clip) continue;
                string name = clip.clip.name;
                if (name.StartsWith(ComboClip) && name.Length == ComboClip.Length + 2)
                {
                    step = name[ComboClip.Length] - '1';
                    clipTime = info.normalizedTime * clip.clip.length;
                    return step >= 0 && step < Moves.Steps;
                }
                if (name == HeavyClip)
                {
                    step = Moves.HeavyStep;
                    clipTime = info.normalizedTime * clip.clip.length;
                    return true;
                }
                if (ClipSwap.IsOwn(name))
                {
                    // one of the katana's own clips (Swing.CustomAnimations): a swing too, with the full-body cut's effects
                    s.OwnClip = true;
                    s.OwnBlending = next;
                    s.OwnStep = ClipSwap.StepOf(name);
                    clipTime = info.normalizedTime * clip.clip.length;
                    other = true;
                    return true;
                }
                if (name.Contains("Attack")) other = true;
            }
            return other;
        }

        private const string ComboClip = "Greatsword BaseAttack (";
        private const string HeavyClip = "Sledge-Attack1"; // the sledge's overhead clip the heavy attack borrows

        // a cut of the combo begins, from what was shown (the stance with the blade sheathed draws it)
        private static void StartCombo(Player player, State s, int step, float clipTime)
        {
            s.MoveFrom = s.HasLast ? s.LastPose : Shown(player, s);
            s.MoveFromScabbard = (step == 0 || step == Moves.HeavyStep) && s.HasLast && s.LastSheathed;
            s.MoveBlend = !s.HasLast;
            s.StrikeSounded = clipTime >= Moves.FullStrikeAt(step);
            s.Feet.Reset();
            if (!Moves.HasFullBody(step)) s.HasLastFooting = false;
            s.Move = 1;
            s.MoveStep = step;
            s.MoveClipStart = clipTime;
            s.MoveStart = Time.time;
        }

        // the flick and the sheathing begin, after hold seconds of the blade held as it is
        private static void StartSheathe(Player player, State s, float hold)
        {
            VisOf(player)?.SetWeaponTrails(false); // the swing's own trail stops where the sheathing takes the blade
            s.MoveFrom = s.HasLast ? s.LastPose : Shown(player, s);
            s.Move = 2;
            s.MoveStart = Time.time;
            s.MoveHold = hold;
            s.MoveClicked = false;
            s.SheatheBlend = s.Weight < 0.99f; // the swing's clip showed, not the stance
        }

        // the sheathing goes on while nothing else takes over: the click when the guard meets the scabbard, and then a
        // put away that waited for it
        private static void StepSheathe(Player player, State s, bool goOn)
        {
            if (!goOn)
            {
                // stopped early (a block, an attack): the stance takes over from the pose shown, not at once
                s.Move = 0;
                s.HidePending = false;
                if (s.HasLast)
                {
                    s.SettleStart = Time.time;
                    s.SettleFrom = s.LastPose;
                }
                return;
            }
            float t = Time.time - s.MoveStart;
            float length = Plugin.StanceNoto.Value;
            if (!s.MoveClicked && t >= Moves.SheathedAt(s.MoveHold, length))
            {
                s.MoveClicked = true;
                ImpactFrame.Click(Mouth(s));
            }
            if (t < Moves.SheatheLength(s.MoveHold, length)) return;
            s.Move = 0;
            s.SettleStart = Time.time;
            s.SettleFrom = s.LastPose;
            if (s.HidePending && player == Player.m_localPlayer)
            {
                s.HidePending = false;
                player.HideHandItems(false, true);
            }
        }

        // the blade as the bones show it now, when nothing of ours was fully shown last frame
        private static Moves.Pose Shown(Player player, State s)
        {
            var root = player.transform.rotation;
            var origin = s.Hips.position - QMath.Rotate(root, StanceSolver.RestHips);
            return new Moves.Pose
            {
                Grip = QMath.Rotate(QMath.Conj(root), s.Attach.position - origin),
                Rot = QMath.Mul(QMath.Conj(root), s.Attach.rotation),
                Along = Plugin.StanceLeftHand.Value,
            };
        }

        // put away was pressed: with the blade out the local player first sheathes it, the game's put away comes after
        internal static bool DelayHide(Player player)
        {
            if (!States.TryGetValue(player, out var s) || !HoldsKatana(player)) return false;
            if (player.IsSwimming() || player.InBed() || player.IsAttached() || !player.IsOnGround()) return false;
            if (s.Move == 0 && !s.InSlash && !s.WasAttacking) return false;
            s.HidePending = true;
            return true;
        }

        // the katana's own swing trail on or off. it is drawn after the pose (DrawTrail), so it follows the cut shown
        private static void Trail(Player player, State s, bool on) => s.TrailOn = on && Plugin.SlashTrailOn.Value;

        private static void DrawTrail(Player player, State s, bool katana)
        {
            var blade = katana ? BladeOf(s) : null;
            if (!blade)
            {
                s.Trail?.Clear();
                return;
            }
            if (s.Trail == null)
            {
                if (!s.TrailOn) return;
                s.Trail = BladeTrail.Make(player.transform, blade.gameObject.layer);
                if (s.Trail == null) return;
            }
            s.Trail.Sample(blade, s.TrailOn, RightItem(VisOf(player)) == FlametalHash);
        }

        private static readonly System.Collections.Generic.List<AnimatorClipInfo> ClipBuffer = new System.Collections.Generic.List<AnimatorClipInfo>();

        // the game's idle, walk and block clips of two-handed swords, with running its jog, run and jump clips too: the
        // ready stance only rests on these, so swings, emotes, sitting and swimming leave it on their own
        internal static bool IsReadyClip(string name, bool running) =>
            name == "Greatsword Idle" || name == "Walk New Greatsword" || name == "Greatsword Crouch Idle" || name == "Greatsword Sneak Walk"
            || name == "IdleTweaked" || name == "Walk New Sword Right" || name == "Crouch Idle" || name == "Sneak walk" // one-handed (Swing.OneHandedMoves)
            || running && (name.StartsWith("Jog New Sword Right") || name == "Run New Sword Right" || name.StartsWith("Jog Strafe Left") || name == "Jog backward")
            || name.StartsWith("Greatsword Strafe") || name.StartsWith("Great sword walk") || name.StartsWith("Block")
            || running && (name.StartsWith("Jog New Greatsword") || name == "Greatsword Jog" || name == "Run New Greatsword" || name.StartsWith("Greatsword Jump"));

        // most of what the base layer plays right now is ready clips
        private static bool ReadyPose(Animator animator)
        {
            animator.GetCurrentAnimatorClipInfo(0, ClipBuffer);
            float ready = 0f, total = 0f;
            foreach (var info in ClipBuffer)
            {
                total += info.weight;
                if (info.clip && IsReadyClip(info.clip.name, Plugin.StanceReadyRunning.Value)) ready += info.weight;
            }
            return total > 0f && ready >= total * 0.6f;
        }

        // the counter's hit stop: the slash pose stops where it is for that long, like the frozen animator under it
        internal static void HoldSlash(Player player, float seconds)
        {
            if (seconds <= 0f || !States.TryGetValue(player, out var s) || s.SlashStart < 0f) return;
            s.HoldAt = Time.time - s.SlashStart;
            s.HoldUntil = Time.time + seconds;
            s.SlashStart += seconds; // after the hold the slash goes on from the same point
        }

        // the local player's swing landed (the game's hit stop began, in the physics step): a full-body cut leaves its
        // slash mark, made after this frame's pose is written (the physics step can run after the animator)
        internal static void HitStop(Player player)
        {
            if (!States.TryGetValue(player, out var s) || !(s.Move == 1 && Moves.HasFullBody(s.MoveStep) || s.WasOwnClip)) return;
            s.MarkPending = true;
        }

        // the mark along the cut, through the blade where it struck
        private static void SlashMark(Player player, State s)
        {
            s.MarkPending = false;
            if (!s.Attach) return;
            var rot = s.Attach.rotation;
            var blade = rot * Vector3.forward;
            var edge = rot * Vector3.right; // the edge leads the cut
            KatanaFx.SlashMark(s.Attach.position + blade * HitAlong, edge, RightItem(VisOf(player)) == FlametalHash);
            if (Plugin.LogCounters.Value) Plugin.Log.LogInfo(s.WasOwnClip ? "own clip cut landed, slash mark shown" : $"full-body cut {s.MoveStep} landed, slash mark shown");
        }

        private const float HitAlong = 1.2f; // meters along the blade from the grip where a cut is taken to strike

        private static readonly ConditionalWeakTable<Player, State> States = new ConditionalWeakTable<Player, State>();
        private static readonly StanceSettings Settings = new StanceSettings();

        internal static bool HoldsKatana(Player player)
        {
            var vis = VisOf(player);
            if (!vis) return false;
            int item = RightItem(vis);
            return item == SteelHash || item == FlametalHash;
        }

        // the katana is out of its scabbard as this client shows it: not resting sheathed in the stance and not past the
        // sheathing's click (the heavy picks its clip by this)
        internal static bool BladeOut(Player player) =>
            !States.TryGetValue(player, out var s) || !(s.HasLast && (s.LastSheathed || s.Move == 2 && s.MoveClicked));

        internal static bool SlashOn => Plugin.StanceEnabled.Value && Plugin.StanceSlash.Value;

        // the local player parried: play the slash here and tell the other clients
        internal static void StartSlash(Player player)
        {
            var s = StateOf(player);
            s.SlashStart = Time.time;
            s.HoldUntil = -1f;
            var zdo = NViewOf(player)?.GetZDO();
            if (zdo == null) return;
            int count = zdo.GetInt(SlashCount, 0) + 1;
            zdo.Set(SlashCount, count, false);
            s.SeenSlashes = count;
        }

        private static State StateOf(Player player)
        {
            if (!States.TryGetValue(player, out var s))
            {
                s = new State();
                States.Add(player, s);
            }
            return s;
        }

        // another client's player slashed: its count went up since this client last looked
        private static void WatchSlashes(Player player, State s)
        {
            var nview = NViewOf(player);
            if (!nview || !nview.IsValid() || nview.IsOwner()) return;
            int count = nview.GetZDO().GetInt(SlashCount, 0);
            if (s.SeenSlashes >= 0 && count != s.SeenSlashes)
            {
                s.SlashStart = Time.time;
                s.HoldUntil = -1f;
            }
            s.SeenSlashes = count;
        }

        internal static void Step(Player player)
        {
            if (!player) return;
            if (!Plugin.StanceEnabled.Value)
            {
                if (States.TryGetValue(player, out var off)) HideSaya(off);
                return;
            }
            bool katana = HoldsKatana(player);
            int back = katana ? 0 : PutAway(player);
            if (!States.TryGetValue(player, out var s))
            {
                if (!katana && back == 0) return;
                s = StateOf(player);
            }
            if (s.Broken || !Bind(player, s)) return;
            StepPose(player, s, katana);
            if (s.MarkPending) SlashMark(player, s);
            HideHand(player, s, katana && s.RollSheathed);
            UpdateSaya(player, s, katana, back);
            UpdateCrackle(s, katana);
            DrawTrail(player, s, katana);
            RecordPose(player, s, katana);
        }

        // Debug.RecordPoses: while the local player holds a katana, the pose 120 times a second in the offline tools'
        // format (each bone's local turn, the hips with their place, the scabbard's place from the hips), the game's own
        // pose before the stance in a second file, what was playing in a third and the settings in a fourth. a new set of
        // files each time the katana is taken out
        private const float RecordStep = 1f / 120f;

        private static void RecordPose(Player player, State s, bool katana)
        {
            if (!katana || s.RecBroken || !Plugin.RecordPoses.Value || player != Player.m_localPlayer || player.IsDead())
            {
                StopRecording(s);
                return;
            }
            try
            {
                if (s.RecPose == null) StartRecording(player, s);
                if (Time.time < s.RecNext) return;
                s.RecNext = Mathf.Max(s.RecNext + RecordStep, Time.time);
                string head = "# rec " + N(Time.time - s.RecStart);
                s.RecPose.WriteLine(head);
                s.RecGame.WriteLine(head);
                for (int i = 0; i < s.Bones.Length; i++)
                {
                    s.RecPose.WriteLine(BoneLine(BoneNames[i], s.Bones[i].localRotation, i == HipsBone ? s.Hips.localPosition : (Vector3?)null));
                    s.RecGame.WriteLine(BoneLine(BoneNames[i], s.Animated[i], i == HipsBone ? s.AnimatedHips : (Vector3?)null));
                }
                if (s.Head)
                {
                    s.RecPose.WriteLine(BoneLine("Head", s.Head.localRotation, null));
                    s.RecGame.WriteLine(BoneLine("Head", s.Head.localRotation, null));
                }
                if (s.HasSheath)
                {
                    var p = s.SayaFollows ? s.FollowPos : s.SheathPos;
                    var r = s.SayaFollows ? s.FollowRot : s.SheathRot;
                    s.RecPose.WriteLine(string.Join(" ", "SAYA", N(p.x), N(p.y), N(p.z), N(r.x), N(r.y), N(r.z), N(r.w)));
                }
                var animator = AnimatorOf(player);
                string state = "-";
                float clipTime = 0f;
                bool blending = false;
                if (animator)
                {
                    blending = animator.IsInTransition(0);
                    animator.GetCurrentAnimatorClipInfo(0, ClipBuffer);
                    var top = ClipBuffer.Where(c => c.clip).OrderByDescending(c => c.weight).FirstOrDefault();
                    if (top.clip)
                    {
                        state = top.clip.name.Replace(' ', '_');
                        clipTime = animator.GetCurrentAnimatorStateInfo(0).normalizedTime * top.clip.length;
                    }
                }
                var pos = player.transform.position;
                s.RecFrames.WriteLine(string.Join(" ", N(Time.time - s.RecStart), state, N(clipTime), N(s.Weight), s.Move, s.OwnClip ? s.OwnStep : -1,
                    blending ? 1 : 0, player.IsBlocking() ? 1 : 0, N(Standing(player)), N(pos.x), N(pos.y), N(pos.z), N(player.transform.eulerAngles.y)));
                if (++s.RecCount % 120 == 0)
                {
                    s.RecPose.Flush();
                    s.RecGame.Flush();
                    s.RecFrames.Flush();
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("pose recording stopped: " + e.Message);
                s.RecBroken = true;
                StopRecording(s);
            }
        }

        private static void StartRecording(Player player, State s)
        {
            string dir = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "IaidoPoses");
            System.IO.Directory.CreateDirectory(dir);
            string name = System.IO.Path.Combine(dir, "pose_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            s.RecPose = new System.IO.StreamWriter(name + ".txt");
            s.RecGame = new System.IO.StreamWriter(name + "_game.txt");
            s.RecFrames = new System.IO.StreamWriter(name + "_frames.txt");
            s.RecFrames.WriteLine("time state clip weight move ownstep blending blocking standing x y z yaw");
            System.IO.File.WriteAllText(name + "_info.txt", string.Join("\n",
                $"{Plugin.ModName} {Plugin.ModVersion}", $"SpineHold {Plugin.StanceSpineHold.Value}", $"ShoulderHold {Plugin.StanceShoulderHold.Value}",
                $"TorsoHold {Plugin.StanceTorsoHold.Value}", $"BladeYaw {Plugin.StanceBladeYaw.Value}", $"BladePitch {Plugin.StanceBladePitch.Value}",
                $"Grip {Plugin.StanceGripSide.Value} {Plugin.StanceGripHeight.Value} {Plugin.StanceGripForward.Value}",
                $"Twist {Plugin.StanceTwist.Value}", $"Lean {Plugin.StanceLean.Value}", $"CustomAnimations {Plugin.CustomAnimations.Value}") + "\n");
            s.Head = s.Bones[Neck].Find("Head");
            s.RecStart = Time.time;
            s.RecNext = Time.time;
            s.RecCount = 0;
            Plugin.Log.LogInfo("recording poses to " + name + ".txt");
        }

        private static void StopRecording(State s)
        {
            if (s.RecPose == null) return;
            s.RecPose.Dispose();
            s.RecGame.Dispose();
            s.RecFrames.Dispose();
            s.RecPose = s.RecGame = s.RecFrames = null;
            Plugin.Log.LogInfo($"pose recording ended, {s.RecCount} poses");
        }

        private static string BoneLine(string name, Quaternion q, Vector3? place) =>
            string.Join(" ", name, N(q.x), N(q.y), N(q.z), N(q.w)) + (place.HasValue ? " " + string.Join(" ", N(place.Value.x), N(place.Value.y), N(place.Value.z)) : "");

        private static string N(float v) => v.ToString("0.#######", System.Globalization.CultureInfo.InvariantCulture);

        // the flametal blade's lightning: on while the blade is drawn, off while it is in its scabbard (the stance, the
        // thunderclap gathering or sheathed again)
        private static void UpdateCrackle(State s, bool katana)
        {
            var blade = katana ? BladeOf(s) : null;
            if (!blade) return;
            if (!s.Lightning || !s.Lightning.transform.IsChildOf(blade))
                s.Lightning = blade.Find(KatanaFx.LightningName)?.gameObject;
            if (!s.Lightning) return;
            KatanaFx.Lightning(s.Lightning, true); // drawn and sheathed alike (the user's wish), the scabbard holds it
        }

        private static void StepPose(Player player, State s, bool katana)
        {
            if (SlashOn) WatchSlashes(player, s);

            bool alive = katana && !player.IsDead();
            // taken out again: it rests in the scabbard at the hip while the hand comes to the grip
            if (alive && !s.HadKatana && s.HasHip && Plugin.ScabbardOn.Value)
            {
                s.TakeOutAt = Time.time;
                s.RollSheathed = true;
            }
            s.HadKatana = katana;
            bool takingOut = alive && s.TakeOutAt >= 0f && Time.time - s.TakeOutAt < TakeOutMax;
            if (!takingOut) s.TakeOutAt = -1f;
            float slashTime = s.SlashStart < 0f ? -1f : Time.time < s.HoldUntil ? s.HoldAt : Time.time - s.SlashStart;
            bool slashing = SlashOn && slashTime >= 0f && slashTime < IaiSlash.Length && alive;
            if (!slashing)
            {
                // zanshin: after the cut the blade stays out a moment, then the flick and the sheathing
                bool cut = s.SlashStart >= 0f && slashTime >= IaiSlash.Length;
                if (cut && alive) StartSheathe(player, s, Plugin.StanceZanshin.Value);
                s.SlashStart = -1f;
            }
            else if (!s.InSlash)
            {
                SlashBegins(s);
                s.ArcShown = false;
            }
            s.InSlash = slashing;
            // the cut passes the middle: its afterimage, on this client for this player
            if (slashing && !s.ArcShown && slashTime >= IaiSlash.HitTime)
            {
                s.ArcShown = true;
                KatanaFx.SlashArc(player.transform, RightItem(VisOf(player)) == FlametalHash);
                if (Plugin.LogCounters.Value) Plugin.Log.LogInfo($"parry afterimage for {player.GetPlayerName()} ({Plugin.CounterAreaAngle.Value:0} degrees, {Plugin.CounterAreaRange.Value:0.#} m)");
            }

            // the flametal thunderclap: its own timeline over the stance, with the crouch
            WatchThunder(player, s);
            WatchHeavy(player, s);
            float thunderTime = s.ThunderStart < 0f ? -1f : Time.time - s.ThunderStart;
            bool thunder = thunderTime >= 0f && thunderTime < Thunder.Length(s.ThunderPrep) && katana && !player.IsDead();
            if (s.ThunderStart >= 0f && !thunder) EndThunder(player, s);
            if (thunder) ThunderEvents(player, s, thunderTime);

            // the katana's own cuts over the game's greatsword swings, read from the animator so it is the same on every
            // client, and the flick and sheathing after any attack
            float clipTime = 0f;
            int step = -1;
            bool otherAttack = false;
            bool attacking = alive && AttackClip(AnimatorOf(player), s, out step, out clipTime, out otherAttack);
            bool combo = attacking && !otherAttack && Moves.Owns(step) && !thunder && !slashing;
            if (combo)
            {
                if (s.Move != 1 || s.MoveStep != step) StartCombo(player, s, step, clipTime);
            }
            else if (s.Move == 1)
            {
                // a full-body cut handed over to the game's next swing fades out from where it was, not from the stance
                if (Moves.HasFullBody(s.MoveStep) && attacking)
                {
                    s.FadeCut = true;
                    s.FadePose = s.LastPose;
                    s.FadeHead = s.LastHead;
                }
                s.Move = 0;
            }
            if (attacking) s.WasAttacking = true;
            else if (s.WasAttacking)
            {
                s.WasAttacking = false;
                if (alive && !Rolling(player) && !thunder && !slashing && s.Move == 0) StartSheathe(player, s, 0f);
            }
            bool rolling = alive && Rolling(player);
            if (s.Move == 2) StepSheathe(player, s, alive && !player.IsBlocking() && !rolling && !thunder && !slashing && !attacking);
            bool moving = s.Move != 0;
            if (moving || slashing || thunder || !attacking) s.FadeCut = false; // another of the katana's own moves takes over, or the swing ended
            bool bladeOut = slashing || moving || (thunder && thunderTime >= s.ThunderPrep && thunderTime < Thunder.SheathedAt(s.ThunderPrep));

            // the trail through each cut: the parry's, the thunderclap's and the combo's
            // one of the katana's own clips playing: its whoosh, bold trail and slash mark like the full-body cut's
            bool ownCut = attacking && s.OwnClip && !thunder && !slashing;
            if (ownCut && (!s.WasOwnClip || clipTime < s.LastOwnTime)) s.StrikeSounded = clipTime >= Moves.FullStrikeAt(s.OwnStep); // a new one began
            s.WasOwnClip = ownCut;
            s.LastOwnTime = clipTime;
            Trail(player, s, (slashing && slashTime >= TrailFrom && slashTime < TrailTo)
                             || (thunder && thunderTime >= s.ThunderPrep && thunderTime < s.ThunderPrep + Thunder.Cut)
                             || (s.Move == 1 && Moves.InCut(s.MoveStep, clipTime))
                             || (ownCut && Moves.InFullCut(s.OwnStep, clipTime)));
            bool fullCut = s.Move == 1 && Moves.HasFullBody(s.MoveStep);
            s.Trail?.Bold(fullCut || ownCut);
            // a full-body cut's own whoosh as its strike begins, on every client (the heavy has its own swing)
            bool heavy = ownCut && (s.OwnStep == Moves.HeavyStep || s.OwnStep == Moves.HeavyOutStep);
            if ((fullCut || ownCut) && !heavy && !s.StrikeSounded && clipTime >= Moves.FullStrikeAt(fullCut ? s.MoveStep : s.OwnStep))
            {
                s.StrikeSounded = true;
                KatanaFx.SlashSound(s.Attach.position, RightItem(VisOf(player)) == FlametalHash);
            }
            if (heavy) HeavyEvents(player, s, clipTime);
            else EndHeavy(s);
            // the hips and legs stand on the cut's own footing during the katana's cuts, the thunderclap has its crouch
            // the cut's own footing (and the heavy's step) while standing. walking or running under an upper body swing
            // the legs are the game's walk, and only the hips turn into the cut
            float bodyWant = s.Move == 1 ? (Plugin.UpperBodySwings.Value ? Standing(player) : 1f) : 0f;
            s.Body = thunder ? 0f : Mathf.MoveTowards(s.Body, bodyWant, Time.deltaTime / (bodyWant > s.Body ? BodyIn : BodyOut));

            // the stance: while blocking, during the katana's own moves, or at rest on the idle and walk clips
            bool free = alive && !player.InAttack() && !rolling;
            bool want = moving || (free && (thunder || player.IsBlocking() || takingOut || (Plugin.StanceReady.Value && ReadyPose(AnimatorOf(player)))));

            // a block snaps into the stance, sheathing after a swing or when coming to rest takes its time
            float blendIn = moving || (free && (player.IsBlocking() || thunder)) ? Plugin.StanceBlendIn.Value : Plugin.StanceSheathe.Value;
            float blend = want ? blendIn : s.FadeCut ? FullFadeOut : Plugin.StanceBlendOut.Value;
            // a full-body cut after one of the game's swings eases in from the game's pose, so nothing snaps
            bool easeIn = fullCut && s.MoveBlend;
            // the sheathing after one of the katana's own clips eases in from the clip's last pose, and an own clip
            // starting from the stance takes over just as gently
            bool easeSheathe = s.Move == 2 && s.SheatheBlend;
            if (ownCut && !want) blend = Mathf.Max(blend, ClipHandover);
            if (!katana && !want) blend = Mathf.Max(blend, PutAwayBlend); // put away: the hand lets go gently
            // an own clip that starts sheathed (the draw, the heavy) keeps the stance until the animator has blended into
            // it: that blend starts from the game's idle, which holds the blade out of the scabbard
            bool holdIntoClip = ownCut && s.OwnBlending && !want && s.Weight > 0f && (s.OwnStep == 0 || s.OwnStep == Moves.HeavyStep);
            if (holdIntoClip && !s.HeldIntoClip && Plugin.LogCounters.Value) Plugin.Log.LogInfo($"stance held through the blend into own clip {s.OwnStep}");
            if (holdIntoClip) s.HeldIntoClip = true;
            else if (!ownCut) s.HeldIntoClip = false;
            s.Weight = holdIntoClip ? s.Weight
                : bladeOut && !easeIn && !easeSheathe ? 1f
                : Mathf.MoveTowards(s.Weight, want ? 1f : 0f, easeIn ? Time.deltaTime / FullBlendIn
                    : easeSheathe ? Time.deltaTime / ClipHandover : blend > 0f ? Time.deltaTime / blend : 1f);
            if (easeIn && s.Weight >= 1f) s.MoveBlend = false;
            if (easeSheathe && s.Weight >= 1f) s.SheatheBlend = false;
            // a roll leaves the katana in its scabbard (the hand lets go of it), until the stance has the hand on the
            // grip again, or a swing or a move takes it
            if (rolling && s.HasHip && Plugin.ScabbardOn.Value) s.RollSheathed = true;
            else if (s.RollSheathed && (!alive || bladeOut || attacking || !want || s.Weight >= 0.99f)) s.RollSheathed = false;

            // the animator runs in the physics step, so some frames it does not write. a bone that still holds what was
            // written here last frame keeps the animator pose saved before
            for (int i = 0; i < s.Bones.Length; i++)
            {
                var local = s.Bones[i].localRotation;
                if (!s.HasWritten || !Same(local, s.Written[i])) s.Animated[i] = local;
            }
            var hips = s.Hips.localPosition;
            if (!s.HasWritten || hips != s.WrittenHips) s.AnimatedHips = hips;

            if (Plugin.DumpAnimationClips.Value && player == Player.m_localPlayer && Time.time >= s.SpineLogAt) LogSpine(player, s);
            if (s.Weight <= 0f)
            {
                if (s.HasWritten)
                {
                    for (int i = 0; i < s.Bones.Length; i++) s.Bones[i].localRotation = s.Animated[i];
                    s.Hips.localPosition = s.AnimatedHips;
                }
                s.HasWritten = false;
                s.HasLast = false;
                s.Body = 0f;
                s.Elbows.Has = false;
                s.FadeCut = false;
                s.HasLastFooting = false;
                return;
            }

            for (int i = 0; i < s.Bones.Length; i++) s.Bones[i].localRotation = s.Animated[i];
            s.Hips.localPosition = s.AnimatedHips;
            ShoulderPlaces(player);
            ReadSettings(Settings);
            if (s.HasSheath)
            {
                Settings.SheathAt = s.SheathRot;
                Settings.SheathGrip = s.SheathPos;
            }
            float crouch = 0f;
            SlashSettings.HeadDown = 0f; // only the thunderclap and the full-body cuts look down, each sets its own
            if (thunder)
            {
                Thunder.Sample(thunderTime, s.ThunderPrep, Settings, SlashSettings, out crouch);
                CopyPose(SlashSettings, Settings);
                Crouch.HipTurn = Settings.HipTurn; // the hips turn into the cut
            }
            else if (slashing)
            {
                IaiSlash.Sample(slashTime, Settings, SlashSettings);
                CopyPose(SlashSettings, Settings);
            }
            else if (s.Move == 1)
            {
                Moves.Combo(s.MoveStep, s.MoveFromScabbard, clipTime, s.MoveClipStart, s.MoveFrom, Settings, SlashSettings);
                CopyPose(SlashSettings, Settings);
            }
            else if (s.Move == 2)
            {
                Moves.Sheathe(Time.time - s.MoveStart, s.MoveHold, Plugin.StanceNoto.Value, s.MoveFrom, Settings, SlashSettings);
                CopyPose(SlashSettings, Settings);
            }
            else if (s.FadeCut)
            {
                Moves.Settle(s.FadePose, 0f, Settings, SlashSettings); // held where the full-body cut ended
                SlashSettings.HeadDown = s.FadeHead;
                CopyPose(SlashSettings, Settings);
            }
            else if (s.SettleStart >= 0f && Time.time - s.SettleStart < SettleSeconds)
            {
                Moves.Settle(s.SettleFrom, (Time.time - s.SettleStart) / SettleSeconds, Settings, SlashSettings);
                CopyPose(SlashSettings, Settings);
            }
            else s.SettleStart = -1f;
            // what is shown now, for the next move to start from
            s.HasLast = s.Weight >= 0.999f;
            s.LastPose = Moves.FromSettings(Settings);
            s.LastHead = Settings.HeadDown;
            s.LastSheathed = !bladeOut && !thunder && !s.FadeCut;
            if (s.Body > 0f && fullCut)
            {
                // a full-body cut: its own feet and hips, the feet kept where they stand while the player moves
                Moves.Footing(s.MoveStep, clipTime, Footing);
                Footing.HipTurn = Settings.HipTurn;
                s.Feet.Step(player.transform.position, player.transform.rotation, Footing, Time.time);
                CrouchSettings.Lerp(Footing, Footing, 0f, s.LastFooting); // kept, the body fades out from where the cut ended
                s.HasLastFooting = true;
            }
            else if (s.Body > 0f && s.Move != 1 && s.HasLastFooting) CrouchSettings.Lerp(s.LastFooting, s.LastFooting, 0f, Footing);
            else
            {
                if (s.Body > 0f) Moves.Footing(s.MoveStep, s.Move == 1 ? clipTime : 0f, Footing); // with the heavy attack's step
                Footing.HipTurn = Settings.HipTurn; // the hips turn into the cut
                if (s.Body <= 0f) s.HasLastFooting = false;
            }
            var full = FullPose(player, s, crouch, s.Move == 1 || s.FadeCut, s.Body > 0f ? Footing : Crouch, s.Body);
            var fullHips = s.Hips.localPosition;
            FollowBlade(player, s);
            float eased = s.Weight * s.Weight * (3f - 2f * s.Weight); // starts and ends gently
            // (a full-body cut fading out holds the blade out of its scabbard, though no move runs)
            if (!bladeOut && !s.FadeCut)
            {
                // the blade's place in the full stance (or the crouch), kept for the scabbard
                var root = player.transform.rotation;
                var origin = s.Hips.position - QMath.Rotate(root, StanceSolver.RestHips);
                s.SheathPos = QMath.Rotate(QMath.Conj(root), s.Attach.position - origin);
                s.SheathRot = QMath.Mul(QMath.Conj(root), s.Attach.rotation);
                s.HasSheath = true;
                // and from the hips bone, once fully in the stance on the ground, for the katana put away
                if (s.Weight >= 1f && !thunder && Upright(player)) LearnHip(s, s.Attach.position, s.Attach.rotation);
            }
            for (int i = 0; i < s.Bones.Length; i++)
            {
                s.Bones[i].localRotation = s.Weight >= 1f ? full[i] : QMath.Nlerp(s.Animated[i], full[i], eased);
                s.Written[i] = s.Bones[i].localRotation;
            }
            s.Hips.localPosition = s.Weight >= 1f ? fullHips : Vector3.Lerp(s.AnimatedHips, fullHips, eased);
            s.WrittenHips = s.Hips.localPosition;
            s.HasWritten = true;
        }

        private static readonly Quaternion[] Pose = new Quaternion[BoneNames.Length];

        private static bool _warnedShoulders;
        private static bool? _loggedFemale;

        // the game's CharacterAnimEvent sets each collarbone's local x in its late update, every frame (the female
        // body's narrower shoulders, 3.5 cm in on each side; the male value is the rest), and every animator step puts
        // the rest back. the arms are solved before that late update, so on the frames the animator ran the hands landed
        // 3.5 cm off the grip on the female and the blade shook (0.2.2, the user). the collarbones go where the late
        // update will put them before the arms are solved, so it changes nothing after
        private static void ShoulderPlaces(Player player)
        {
            try
            {
                var anim = AnimEventOf(player);
                if (!anim || !FemaleHack(anim)) return;
                var vis = AnimEventVis(anim);
                bool female = vis && vis.GetModelIndex() == 1;
                float offset = female ? FemaleOffset(anim) : MaleOffset(anim);
                if (Plugin.LogCounters.Value && player == Player.m_localPlayer && female != _loggedFemale)
                {
                    _loggedFemale = female;
                    Plugin.Log.LogInfo($"iai stance: {(female ? "female" : "male")} body, collarbones placed at the game's x {offset} before the arms are solved");
                }
                var left = LeftShoulderOf(anim);
                var right = RightShoulderOf(anim);
                if (left)
                {
                    var p = left.localPosition;
                    p.x = -offset;
                    left.localPosition = p;
                }
                if (right)
                {
                    var p = right.localPosition;
                    p.x = offset;
                    right.localPosition = p;
                }
            }
            catch (System.Exception e)
            {
                if (_warnedShoulders) return;
                _warnedShoulders = true;
                Plugin.Log.LogWarning("iai stance: the game's shoulder places could not be read, the female stance may shake: " + e.Message);
            }
        }

        // while the blade slides into its scabbard, the scabbard takes the blade's real place slid back in by how far
        // it is still out. the arm may fall a little short of the sheathing's aim; then blade and scabbard stay one, and
        // nothing is left to snap when the stance takes over. eased in over FollowEase as the sliding starts
        private static void FollowBlade(Player player, State s)
        {
            float slide = s.Move == 2 ? Moves.SlideOut(Time.time - s.MoveStart, s.MoveHold, Plugin.StanceNoto.Value) : -1f;
            if (slide < 0f || !s.HasSheath)
            {
                s.SayaFollows = false;
                s.FollowStart = -1f;
                return;
            }
            var root = player.transform.rotation;
            var origin = s.Hips.position - QMath.Rotate(root, StanceSolver.RestHips);
            var pos = QMath.Rotate(QMath.Conj(root), s.Attach.position - origin);
            var rot = QMath.Mul(QMath.Conj(root), s.Attach.rotation);
            Moves.Slide(ref pos, ref rot, -slide);
            if (s.FollowStart < 0f) s.FollowStart = Time.time;
            float ease = Mathf.Clamp01((Time.time - s.FollowStart) / FollowEase);
            ease = ease * ease * (3f - 2f * ease);
            s.FollowPos = Vector3.Lerp(s.SheathPos, pos, ease);
            s.FollowRot = QMath.Nlerp(s.SheathRot, rot, ease);
            s.SayaFollows = true;
        }

        // the whole stance from the animator pose: the thunderclap's crouch, or during the katana's cuts the body on
        // their own footing (body, from rest), first (hips down, legs onto their feet), then the spine turn, the head
        // kept forward, and both arms
        private static Quaternion[] FullPose(Player player, State s, float crouch, bool restSpine, CrouchSettings legs, float body)
        {
            var root = player.transform.rotation;
            var b = s.Bones;
            var hipTip = new Quaternion(0f, 0f, 0f, 1f);
            if (restSpine)
            {
                b[SpineLow].localRotation = RestSpine;
                b[Spine1].localRotation = RestSpine1;
                b[Spine2].localRotation = RestSpine2;
                b[Neck].localRotation = RestNeck;
            }
            else
            {
                // the game's idle, walk and run hunch the upper body forward, and the arms were made on the spine at rest:
                // the stance holds it that way by SpineHold, so the hands land where they were made to
                float hold = Mathf.Clamp01(Plugin.StanceSpineHold.Value);
                var chestBefore = b[Spine2].rotation;
                b[SpineLow].localRotation = QMath.Nlerp(b[SpineLow].localRotation, RestSpine, hold);
                b[Spine1].localRotation = QMath.Nlerp(b[Spine1].localRotation, RestSpine1, hold);
                b[Spine2].localRotation = QMath.Nlerp(b[Spine2].localRotation, RestSpine2, hold);
                // the walk and run turn the neck against the chest's sway to keep the head steady. the hold took the sway
                // away, so the neck gets back the turn the hold took from the chest, or the head would sway alone (55
                // degrees running, the game's own run 4, measured in Unity on the game's avatar)
                var taken = QMath.Mul(b[Spine2].rotation, QMath.Conj(chestBefore));
                b[Neck].rotation = QMath.Mul(QMath.Conj(taken), b[Neck].rotation);
                if (body <= 0f && crouch <= 0f) HoldTorso(player, s, root);
            }
            if (body > 0f)
            {
                // the greatsword swing under the cut twists and bends the hips, so they and the legs start from rest
                for (int i = HipsBone; i <= RFoot; i++) KeptBody[i - HipsBone] = b[i].localRotation;
                var keptHips = s.Hips.localPosition;
                b[HipsBone].localRotation = RestHipsRot;
                s.Hips.localPosition = RestHipsLocal;
                for (int i = LUp; i <= RFoot; i++) b[i].localRotation = RestLegs[i - LUp];
                Sink(player, s, 1f, legs);
                if (body < 1f)
                {
                    for (int i = HipsBone; i <= RFoot; i++) b[i].localRotation = QMath.Nlerp(KeptBody[i - HipsBone], b[i].localRotation, body);
                    s.Hips.localPosition = Vector3.Lerp(keptHips, s.Hips.localPosition, body);
                }
                hipTip = StanceSolver.Turn(root, QMath.Euler(legs.HipPitch * body, legs.HipTurn * body, 0f));
            }
            else if (crouch > 0f)
            {
                Sink(player, s, crouch, legs);
                hipTip = StanceSolver.Turn(root, QMath.Euler(legs.HipPitch * crouch, legs.HipTurn * crouch, 0f));
            }
            // walking under a cut: the hips still turn into it, the feet stay where the walk put them
            float drive = restSpine ? Settings.HipTurn * (1f - body) : 0f;
            if (drive != 0f)
            {
                Drive(player, s, drive);
                hipTip = QMath.Mul(StanceSolver.Turn(root, QMath.Euler(0f, drive, 0f)), hipTip);
            }
            var turn = StanceSolver.Spine(root, Settings);
            var half = StanceSolver.Half(turn);
            b[Spine1].rotation = QMath.Mul(half, b[Spine1].rotation);
            b[Spine2].rotation = QMath.Mul(half, b[Spine2].rotation);
            if (Settings.HeadForward) b[Neck].rotation = QMath.Mul(QMath.Conj(QMath.Mul(StanceSolver.Head(root, Settings), hipTip)), b[Neck].rotation);

            var right = ArmOf(b[RArm], b[RFore], b[RHand]);
            var left = ArmOf(b[LArm], b[LFore], b[LHand]);
            var origin = s.Hips.position - QMath.Rotate(root, StanceSolver.RestHips);
            s.Elbows.MaxTurn = (s.Move == 1 && Moves.HasFullBody(s.MoveStep) ? FullElbowTurnSpeed : ElbowTurnSpeed) * Time.deltaTime;
            StanceSolver.Solve(origin, root, Settings, ref right, ref left, s.Attach.localPosition, s.Attach.localRotation, b[RHand].lossyScale.x, s.Elbows);
            b[RArm].rotation = right.Upper;
            b[RFore].rotation = right.Lower;
            b[RHand].rotation = right.Hand;
            b[LArm].rotation = left.Upper;
            b[LFore].rotation = left.Lower;
            b[LHand].rotation = left.Hand;
            for (int i = 0; i < b.Length; i++) Pose[i] = b[i].localRotation;
            return Pose;
        }

        // the game's idle, walk, run and block turn the chest each their own way (the block strafes swing the whole
        // body, each running stride sways it 22): the chest turns back to face ahead (TorsoHold), evenly over the three
        // spine bones. the head stays where the game had it and the legs keep their steps. the collarbones, which the
        // walk and run rock under the hands on the grip, are held toward rest (ShoulderHold)
        private static void HoldTorso(Player player, State s, Quaternion root)
        {
            var b = s.Bones;
            bool blocking = player.IsBlocking();
            float hold = Mathf.Clamp01(Plugin.StanceTorsoHold.Value);
            s.BlockHipsWeight = Mathf.MoveTowards(s.BlockHipsWeight, blocking ? 1f : 0f, Time.deltaTime / BlockHipsBlend);
            if (s.BlockHipsWeight > 0f && hold > 0f)
            {
                float hipsYaw = StanceSolver.Yaw(root, b[HipsBone].rotation);
                if (blocking && Standing(player) > 0.95f) s.BlockHipsYaw = hipsYaw;
                float back = Mathf.Clamp(Mathf.DeltaAngle(s.BlockHipsYaw, hipsYaw) * HipsHoldShare, -HipsHoldMax, HipsHoldMax) * hold * s.BlockHipsWeight;
                if (back != 0f)
                {
                    var keepNeck = b[Neck].rotation;
                    Drive(player, s, -back);
                    b[Neck].rotation = keepNeck;
                }
            }
            // the chest square to the facing (the game's idle turns it about 8 degrees, the block about -25, the run sways
            // it), so the stance's own turn and the arms sit where they were made
            float counter = StanceSolver.ChestBack(StanceSolver.Yaw(root, b[Spine2].rotation), hold);
            if (counter != 0f)
            {
                var neck = b[Neck].rotation;
                var third = StanceSolver.Turn(root, QMath.Euler(0f, -counter / 3f, 0f));
                b[SpineLow].rotation = QMath.Mul(third, b[SpineLow].rotation);
                b[Spine1].rotation = QMath.Mul(third, b[Spine1].rotation);
                b[Spine2].rotation = QMath.Mul(third, b[Spine2].rotation);
                b[Neck].rotation = neck;
            }
            float clavicles = Mathf.Clamp01(Plugin.StanceShoulderHold.Value);
            b[RClavicle].localRotation = QMath.Nlerp(b[RClavicle].localRotation, RestRClavicle, clavicles);
            b[LClavicle].localRotation = QMath.Nlerp(b[LClavicle].localRotation, RestLClavicle, clavicles);
            b[LClavicle].rotation = QMath.Mul(StanceSolver.Turn(root, QMath.Euler(0f, 0f, -LeftShoulderLift)), b[LClavicle].rotation);
        }

        // the hips turned about the body's upright by degrees, each leg back onto the foot it stood on
        private static void Drive(Player player, State s, float degrees)
        {
            var root = player.transform.rotation;
            var rootPos = player.transform.position;
            var b = s.Bones;
            var left = ArmOf(b[LUp], b[LLow], b[LFoot]);
            var right = ArmOf(b[RUp], b[RLow], b[RFoot]);
            b[HipsBone].rotation = QMath.Mul(StanceSolver.Turn(root, QMath.Euler(0f, degrees, 0f)), b[HipsBone].rotation);
            var leftNow = ArmOf(b[LUp], b[LLow], b[LFoot]);
            StanceSolver.Leg(ref leftNow, left, rootPos, root, Vector3.zero, 0f, Vector3.zero, 0f);
            b[LUp].rotation = leftNow.Upper;
            b[LLow].rotation = leftNow.Lower;
            b[LFoot].rotation = leftNow.Hand;
            var rightNow = ArmOf(b[RUp], b[RLow], b[RFoot]);
            StanceSolver.Leg(ref rightNow, right, rootPos, root, Vector3.zero, 0f, Vector3.zero, 0f);
            b[RUp].rotation = rightNow.Upper;
            b[RLow].rotation = rightNow.Lower;
            b[RFoot].rotation = rightNow.Hand;
        }

        private static int _forwardSpeed, _sidewaySpeed;

        // 1 standing still, 0 walking or faster, from the animator's speed (every client has it)
        private static float Standing(Player player)
        {
            var animator = AnimatorOf(player);
            if (!animator) return 1f;
            if (_forwardSpeed == 0)
            {
                _forwardSpeed = Animator.StringToHash("forward_speed");
                _sidewaySpeed = Animator.StringToHash("sideway_speed");
            }
            float f = animator.GetFloat(_forwardSpeed), side = animator.GetFloat(_sidewaySpeed);
            return 1f - Mathf.Clamp01((Mathf.Sqrt(f * f + side * side) - 0.2f) / 0.6f);
        }

        // hips down and tipped, then each leg onto its foot, weight 0 leaves them as they are
        private static void Sink(Player player, State s, float weight, CrouchSettings legs)
        {
            var root = player.transform.rotation;
            var rootPos = player.transform.position;
            var b = s.Bones;
            var animatedLeft = ArmOf(b[LUp], b[LLow], b[LFoot]);
            var animatedRight = ArmOf(b[RUp], b[RLow], b[RFoot]);
            var hipsPos = s.Hips.position;
            var hipsRot = s.Hips.rotation;
            StanceSolver.Hips(root, legs, weight, ref hipsPos, ref hipsRot);
            s.Hips.SetPositionAndRotation(hipsPos, hipsRot);
            var leftLeg = ArmOf(b[LUp], b[LLow], b[LFoot]);
            StanceSolver.Leg(ref leftLeg, animatedLeft, rootPos, root, legs.LeftFoot, legs.LeftFootTurn, legs.LeftKnee, weight);
            b[LUp].rotation = leftLeg.Upper;
            b[LLow].rotation = leftLeg.Lower;
            b[LFoot].rotation = leftLeg.Hand;
            var rightLeg = ArmOf(b[RUp], b[RLow], b[RFoot]);
            StanceSolver.Leg(ref rightLeg, animatedRight, rootPos, root, legs.RightFoot, legs.RightFootTurn, legs.RightKnee, weight);
            b[RUp].rotation = rightLeg.Upper;
            b[RLow].rotation = rightLeg.Lower;
            b[RFoot].rotation = rightLeg.Hand;
        }

        private static Arm ArmOf(Transform upper, Transform lower, Transform hand) => new Arm
        {
            Shoulder = upper.position, Elbow = lower.position, Wrist = hand.position,
            Upper = upper.rotation, Lower = lower.rotation, Hand = hand.rotation
        };

        private static readonly StanceSettings SlashSettings = new StanceSettings();

        private static void CopyPose(StanceSettings from, StanceSettings to)
        {
            to.Grip = from.Grip;
            to.BladeYaw = from.BladeYaw;
            to.BladePitch = from.BladePitch;
            to.EdgeRoll = from.EdgeRoll;
            to.SpineTwist = from.SpineTwist;
            to.SpineLean = from.SpineLean;
            to.LeftOnBlade = from.LeftOnBlade;
            to.LeftAlongBlade = from.LeftAlongBlade;
            to.LeftHand = from.LeftHand;
            to.MouthAxis = from.MouthAxis;
            to.RightElbow = from.RightElbow; // the thunderclap's crouch has its own left elbow
            to.LeftElbow = from.LeftElbow;
            to.HipTurn = from.HipTurn;
            to.HeadDown = from.HeadDown;
            to.Attach = from.Attach;
        }

        private static readonly StanceSettings Defaults = new StanceSettings();
        private static readonly Vector3 DefaultLeftHand = Defaults.LeftHand;

        // the stance from the config, read every frame so changes show at once
        private static void ReadSettings(StanceSettings into)
        {
            into.Attach = null;
            into.SheathAt = null;
            into.LeftHand = DefaultLeftHand;
            into.MouthAxis = Vector3.zero;
            into.HipTurn = 0f;
            into.HeadDown = 0f;
            into.RightElbow = Defaults.RightElbow;
            into.LeftElbow = Defaults.LeftElbow;
            into.EdgeRoll = Defaults.EdgeRoll;
            into.LeftOnBlade = 1f;
            into.Grip = new Vector3(Plugin.StanceGripSide.Value, Plugin.StanceGripHeight.Value, Plugin.StanceGripForward.Value);
            into.BladeYaw = Plugin.StanceBladeYaw.Value;
            into.BladePitch = Plugin.StanceBladePitch.Value;
            into.SpineTwist = Plugin.StanceTwist.Value;
            into.SpineLean = Plugin.StanceLean.Value;
            into.LeftAlongBlade = Plugin.StanceLeftHand.Value;
        }

        // finds the bones once per player, by the names of the game's player skeleton
        private static bool Bind(Player player, State s)
        {
            if (s.Bones != null && s.Bones[0]) return true;
            var animator = AnimatorOf(player);
            if (!animator) return false;
            var root = animator.transform;
            s.Bones = new Transform[BoneNames.Length];
            for (int i = 0; i < BoneNames.Length; i++)
                if (!(s.Bones[i] = Find(root, BoneNames[i]))) return Fail(s, BoneNames[i]);
            s.Hips = Find(root, "Hips");
            s.Attach = Find(root, "RightHand_Attach");
            if (!s.Hips) return Fail(s, "Hips");
            if (!s.Attach) return Fail(s, "RightHand_Attach");
            s.HasWritten = false;
            return true;
        }

        private static bool Fail(State s, string bone)
        {
            s.Broken = true;
            Plugin.Log.LogWarning($"iai stance: player bone '{bone}' not found, the stance is off for this player");
            return false;
        }

        private static Transform Find(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            foreach (Transform child in parent)
            {
                var hit = Find(child, name);
                if (hit) return hit;
            }
            return null;
        }

        private static bool Same(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
    }
}
