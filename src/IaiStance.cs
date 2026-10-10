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
        // the cut's trail runs from the draw to the end of the cut, then fades out in its colours
        private const float TrailFrom = 0.04f, TrailTo = 0.2f;
        // counts the slashes of a player in its network data, so every client sees each one
        private static readonly int SlashCount = "IaidoKatana_slash".GetStableHashCode();
        // the same for the flametal thunderclap: how many were started, the last one called off, and its gathering time
        private static readonly int ThunderCount = "IaidoKatana_thunder".GetStableHashCode();
        private static readonly int ThunderStop = "IaidoKatana_thunder_stop".GetStableHashCode();
        private static readonly int ThunderPrepKey = "IaidoKatana_thunder_prep".GetStableHashCode();
        private static readonly CrouchSettings Crouch = new CrouchSettings();
        private static readonly int SteelHash = Katana.SteelPrefab.GetStableHashCode();
        private static readonly int FlametalHash = Katana.FlametalPrefab.GetStableHashCode();

        // spine first, then the arms, the order the pose is built in. the hips and legs only move in the thunderclap's crouch
        // and under the katana's own cuts
        private static readonly string[] BoneNames = { "Spine1", "Spine2", "Neck", "RightArm", "RightForeArm", "RightHand", "LeftArm", "LeftForeArm", "LeftHand",
                                                       "Hips", "LeftUpLeg", "LeftLeg", "LeftFoot", "RightUpLeg", "RightLeg", "RightFoot", "Spine" };
        private const int Spine1 = 0, Spine2 = 1, Neck = 2, RArm = 3, RFore = 4, RHand = 5, LArm = 6, LFore = 7, LHand = 8;
        private const int HipsBone = 9, LUp = 10, LLow = 11, LFoot = 12, RUp = 13, RLow = 14, RFoot = 15, SpineLow = 16;

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
        private static readonly CrouchSettings Footing = new CrouchSettings();
        private static readonly Quaternion[] KeptBody = new Quaternion[RFoot - HipsBone + 1];

        // the spine of the ripped player at rest. the katana's own cuts start from it, so the greatsword swing playing
        // under them can not twist the upper body away from the cut
        private static readonly Quaternion RestSpine = new Quaternion(-0.03922647f, 0f, 0f, 0.9992304f);
        private static readonly Quaternion RestSpine1 = new Quaternion(-0.010170685f, 0f, 0f, 0.9999483f);
        private static readonly Quaternion RestSpine2 = new Quaternion(0f, 0f, 0f, 1f);
        private static readonly Quaternion RestNeck = new Quaternion(0.34641975f, -0.008183268f, 0.026077881f, 0.9376814f);

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
            internal Vector3 AnimatedHips;     // the hips' place from the animator, and what the crouch wrote
            internal Vector3 WrittenHips;
        }

        // the local player started the thunderclap: pose it here and tell the other clients
        internal static void StartThunder(Player player, float prep)
        {
            var s = StateOf(player);
            s.ThunderStart = Time.time;
            s.ThunderPrep = prep;
            s.ThunderStage = 0;
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
            }
            s.SeenThunder = count;
            if (s.ThunderStart >= 0f && zdo.GetInt(ThunderStop, 0) == count) EndThunder(player, s);
        }

        // the glint as the blade leaves, and the click when it is back in its scabbard
        private static void ThunderEvents(Player player, State s, float t)
        {
            float prep = s.ThunderPrep;
            if (s.ThunderStage == 0 && t >= prep)
            {
                s.ThunderStage = 1;
                KatanaFx.Glint(Mouth(s));
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
            s.Sheathed.SetActive(holster);

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
            if (holster && s.HasHip)
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

        // which greatsword clip the base layer is playing or blending into: a cut of the combo (step 0 to 2, and how far
        // into its clip), or another attack (the heavy one)
        private static bool AttackClip(Animator animator, out int step, out float clipTime, out bool other)
        {
            step = -1;
            clipTime = 0f;
            other = false;
            if (!animator) return false;
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
        }

        // the sheathing goes on while nothing else takes over: the click when the guard meets the scabbard, and then a
        // put away that waited for it
        private static void StepSheathe(Player player, State s, bool goOn)
        {
            if (!goOn)
            {
                s.Move = 0;
                s.HidePending = false;
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

        private static readonly ConditionalWeakTable<Player, State> States = new ConditionalWeakTable<Player, State>();
        private static readonly StanceSettings Settings = new StanceSettings();

        internal static bool HoldsKatana(Player player)
        {
            var vis = VisOf(player);
            if (!vis) return false;
            int item = RightItem(vis);
            return item == SteelHash || item == FlametalHash;
        }

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
            UpdateSaya(player, s, katana, back);
            UpdateCrackle(s, katana);
            DrawTrail(player, s, katana);
        }

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
            float slashTime = s.SlashStart < 0f ? -1f : Time.time < s.HoldUntil ? s.HoldAt : Time.time - s.SlashStart;
            bool slashing = SlashOn && slashTime >= 0f && slashTime < IaiSlash.Length && alive;
            if (!slashing)
            {
                // zanshin: after the cut the blade stays out a moment, then the flick and the sheathing
                bool cut = s.SlashStart >= 0f && slashTime >= IaiSlash.Length;
                if (cut && alive) StartSheathe(player, s, Plugin.StanceZanshin.Value);
                s.SlashStart = -1f;
            }
            else if (!s.InSlash) SlashBegins(s);
            s.InSlash = slashing;

            // the flametal thunderclap: its own timeline over the stance, with the crouch
            WatchThunder(player, s);
            float thunderTime = s.ThunderStart < 0f ? -1f : Time.time - s.ThunderStart;
            bool thunder = thunderTime >= 0f && thunderTime < Thunder.Length(s.ThunderPrep) && katana && !player.IsDead();
            if (s.ThunderStart >= 0f && !thunder) EndThunder(player, s);
            if (thunder) ThunderEvents(player, s, thunderTime);

            // the katana's own cuts over the game's greatsword swings, read from the animator so it is the same on every
            // client, and the flick and sheathing after any attack
            float clipTime = 0f;
            int step = -1;
            bool otherAttack = false;
            bool attacking = alive && AttackClip(AnimatorOf(player), out step, out clipTime, out otherAttack);
            bool combo = attacking && !otherAttack && Moves.OwnCuts && !thunder && !slashing;
            if (combo)
            {
                if (s.Move != 1 || s.MoveStep != step) StartCombo(player, s, step, clipTime);
            }
            else if (s.Move == 1) s.Move = 0;
            if (attacking) s.WasAttacking = true;
            else if (s.WasAttacking)
            {
                s.WasAttacking = false;
                if (alive && !player.InDodge() && !thunder && !slashing && s.Move == 0) StartSheathe(player, s, 0f);
            }
            if (s.Move == 2) StepSheathe(player, s, alive && !player.IsBlocking() && !player.InDodge() && !thunder && !slashing && !attacking);
            bool moving = s.Move != 0;
            bool bladeOut = slashing || moving || (thunder && thunderTime >= s.ThunderPrep && thunderTime < Thunder.SheathedAt(s.ThunderPrep));

            // the trail through each cut: the parry's, the thunderclap's and the combo's
            Trail(player, s, (slashing && slashTime >= TrailFrom && slashTime < TrailTo)
                             || (thunder && thunderTime >= s.ThunderPrep && thunderTime < s.ThunderPrep + Thunder.Cut)
                             || (s.Move == 1 && Moves.InCut(s.MoveStep, clipTime)));
            // the hips and legs stand on the cut's own footing during the katana's cuts, the thunderclap has its crouch
            float bodyWant = s.Move == 1 ? 1f : 0f;
            s.Body = thunder ? 0f : Mathf.MoveTowards(s.Body, bodyWant, Time.deltaTime / (bodyWant > s.Body ? BodyIn : BodyOut));

            // the stance: while blocking, during the katana's own moves, or at rest on the idle and walk clips
            bool free = alive && !player.InAttack() && !player.InDodge();
            bool want = moving || (free && (thunder || player.IsBlocking() || (Plugin.StanceReady.Value && ReadyPose(AnimatorOf(player)))));

            // a block snaps into the stance, sheathing after a swing or when coming to rest takes its time
            float blendIn = moving || (free && (player.IsBlocking() || thunder)) ? Plugin.StanceBlendIn.Value : Plugin.StanceSheathe.Value;
            float blend = want ? blendIn : Plugin.StanceBlendOut.Value;
            s.Weight = bladeOut ? 1f : Mathf.MoveTowards(s.Weight, want ? 1f : 0f, blend > 0f ? Time.deltaTime / blend : 1f);

            // the animator runs in the physics step, so some frames it does not write. a bone that still holds what was
            // written here last frame keeps the animator pose saved before
            for (int i = 0; i < s.Bones.Length; i++)
            {
                var local = s.Bones[i].localRotation;
                if (!s.HasWritten || !Same(local, s.Written[i])) s.Animated[i] = local;
            }
            var hips = s.Hips.localPosition;
            if (!s.HasWritten || hips != s.WrittenHips) s.AnimatedHips = hips;

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
                return;
            }

            for (int i = 0; i < s.Bones.Length; i++) s.Bones[i].localRotation = s.Animated[i];
            s.Hips.localPosition = s.AnimatedHips;
            ReadSettings(Settings);
            if (s.HasSheath)
            {
                Settings.SheathAt = s.SheathRot;
                Settings.SheathGrip = s.SheathPos;
            }
            float crouch = 0f;
            if (thunder)
            {
                Thunder.Sample(thunderTime, s.ThunderPrep, Settings, SlashSettings, out crouch);
                CopyPose(SlashSettings, Settings);
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
            else if (s.SettleStart >= 0f && Time.time - s.SettleStart < SettleSeconds && s.Weight >= 1f)
            {
                Moves.Settle(s.SettleFrom, (Time.time - s.SettleStart) / SettleSeconds, Settings, SlashSettings);
                CopyPose(SlashSettings, Settings);
            }
            else s.SettleStart = -1f;
            // what is shown now, for the next move to start from
            s.HasLast = s.Weight >= 0.999f;
            s.LastPose = Moves.FromSettings(Settings);
            s.LastSheathed = !bladeOut && !thunder;
            if (s.Body > 0f) Moves.Footing(s.MoveStep, s.Move == 1 ? clipTime : 0f, Footing); // with the heavy attack's step
            var full = FullPose(player, s, crouch, s.Move == 1, s.Body > 0f ? Footing : Crouch, s.Body);
            var fullHips = s.Hips.localPosition;
            FollowBlade(player, s);
            float eased = s.Weight * s.Weight * (3f - 2f * s.Weight); // starts and ends gently
            if (!bladeOut)
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
            var turn = StanceSolver.Spine(root, Settings);
            var half = StanceSolver.Half(turn);
            b[Spine1].rotation = QMath.Mul(half, b[Spine1].rotation);
            b[Spine2].rotation = QMath.Mul(half, b[Spine2].rotation);
            if (Settings.HeadForward) b[Neck].rotation = QMath.Mul(QMath.Conj(QMath.Mul(turn, hipTip)), b[Neck].rotation);

            var right = ArmOf(b[RArm], b[RFore], b[RHand]);
            var left = ArmOf(b[LArm], b[LFore], b[LHand]);
            var origin = s.Hips.position - QMath.Rotate(root, StanceSolver.RestHips);
            StanceSolver.Solve(origin, root, Settings, ref right, ref left, s.Attach.localPosition, s.Attach.localRotation, b[RHand].lossyScale.x);
            b[RArm].rotation = right.Upper;
            b[RFore].rotation = right.Lower;
            b[RHand].rotation = right.Hand;
            b[LArm].rotation = left.Upper;
            b[LFore].rotation = left.Lower;
            b[LHand].rotation = left.Hand;
            for (int i = 0; i < b.Length; i++) Pose[i] = b[i].localRotation;
            return Pose;
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
            to.Attach = from.Attach;
        }

        private static readonly Vector3 DefaultLeftHand = new StanceSettings().LeftHand;

        // the stance from the config, read every frame so changes show at once
        private static void ReadSettings(StanceSettings into)
        {
            into.Attach = null;
            into.SheathAt = null;
            into.LeftHand = DefaultLeftHand;
            into.EdgeRoll = 0f;
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
