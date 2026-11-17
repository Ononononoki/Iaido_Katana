using UnityEngine;

namespace Iaido_Katana
{
    // what one player's daisho shows on this client: the move playing, the wakizashi at the hip and in the hand
    internal sealed class DaishoPlay
    {
        internal DaishoMove Move;          // the move playing, null when none
        internal bool IsParry;
        internal float Start = -1f;
        internal float Began = -1f;        // when it began on the clock (Start moves with each hit stop)
        internal bool WakiOut;             // the wakizashi left its scabbard in this move
        internal float HoldUntil = -1f;    // the move stands still until then (a hit stop)
        internal float HoldAt;
        internal float T;                  // seconds into the move, this frame
        internal int Rig;                  // 0 the male skeleton, 1 the female
        internal int NextCut;              // the next event of the move to happen
        internal int SeenParry = -1, SeenRanbu = -1;
        internal readonly Quaternion[] Last = new Quaternion[DaishoData.BoneCount]; // the last pose written, for the hand over
        internal Vector3 LastHips;
        internal float TailStart = -1f;    // the move ended: the hand over from its last pose began then
        internal Vector3 WakiPos, SayaPos; // the wakizashi and its scabbard this frame, in the player's frame (feet origin)
        internal Quaternion WakiRot, SayaRot;
        internal float Hold;               // how much the left hand holds the wakizashi
        internal bool HasWaki;
        internal GameObject Saya, Blade;   // the scabbard and the wakizashi
        internal BladeTrail Trail;         // the wakizashi's own trail
        internal bool TrailOn;
        internal bool Failed;              // an effect threw, logged once
    }

    internal static partial class IaiStance
    {
        private sealed partial class State
        {
            internal readonly DaishoPlay Daisho = new DaishoPlay();
        }

        private static readonly int DaishoParryCount = "IaidoKatana_dparry".GetStableHashCode();
        private static readonly int DaishoRanbuCount = "IaidoKatana_dranbu".GetStableHashCode();
        private static readonly int DaishoRanbuStop = "IaidoKatana_dranbu_stop".GetStableHashCode();
        private static readonly Quaternion[] DaishoBones = new Quaternion[DaishoData.BoneCount];
        // the file's bone order (DaishoData.BoneNames) in this class's
        private static readonly int[] DaishoMap = { HipsBone, LUp, LLow, LFoot, RUp, RLow, RFoot, Spine1, Spine2, Neck, RArm, RFore, RHand, LArm, LFore, LHand };
        private const int DaishoLegs = 7; // the first seven of them are the hips and legs

        // the wakizashi's grip in the left hand's frame and its attach frame turned from it, measured on every frame the hand
        // holds it in the poses (devtools\session_2026-11-08\fk_check.py: the same on all of them and on both skeletons)
        private static readonly Vector3 DaishoHandOffset = new Vector3(0f, 0.0998f, 0.0285f);
        private static readonly Quaternion DaishoHandTurn = new Quaternion(0.5f, 0.5f, 0.5f, 0.5f);
        // the wakizashi mesh's node in its attach frame: the mesh's y runs along the blade from the pommel, the hand 0.19 m up it
        private static readonly Vector3 DaishoNodePosition = new Vector3(0f, 0f, -0.19f);
        // the scabbard at the right hip when no move plays, in the hips' carried frame (the packed moves' key)
        private static readonly Vector3 DaishoHipPosition = new Vector3(0.10f, 1.075f, 0.40f);
        // the scabbard stood 16 cm off the hip there on both bodies (the owner, 15 Nov: it sticks out too much). this
        // much nearer, at rest and through the moves, which leaves 6 cm for armour
        private static readonly Vector3 DaishoHipIn = new Vector3(-0.08f, 0f, -0.08f);
        // the Ranbu's first pose comes in over this long from the pose shown, the parry's at once
        private const float DaishoEaseIn = 0.15f;
        // the wakizashi counts as drawn this far from its scabbard's place, and as back in it this near
        private const float WakiOutAt = 0.05f, WakiBackAt = 0.01f;
        // the hand over from a move's last pose back to the sheathing: the upper body, then the legs a little longer
        // the moves end in the ready stance already; the katana arm and the shoulders still hand over to the sheathing slowly
        // (the owner, 18 Nov: the end of the Ranbu was not easing enough)
        private const float DaishoTailUpper = 0.60f, DaishoTailLegs = 0.60f;
        private const float DaishoHold = 0.2f; // the katana held out this long before the sheathing

        private static int RigOf(Player player)
        {
            var vis = VisOf(player);
            return vis && vis.GetModelIndex() == 1 ? 1 : 0;
        }

        // the local player parried with the daisho's katana, or started the Ranbu: play it here and tell the other clients
        internal static void StartDaishoParry(Player player) => BeginDaisho(player, DaishoData.Parry, true);

        internal static void StartRanbu(Player player) => BeginDaisho(player, DaishoData.Ranbu, false);

        private static void BeginDaisho(Player player, DaishoMove move, bool parry)
        {
            if (move == null) return;
            var s = StateOf(player);
            PlayDaisho(player, s, move, parry);
            var zdo = NViewOf(player)?.GetZDO();
            if (zdo == null) return;
            int key = parry ? DaishoParryCount : DaishoRanbuCount;
            int count = zdo.GetInt(key, 0) + 1;
            zdo.Set(key, count, false);
            if (parry) s.Daisho.SeenParry = count;
            else s.Daisho.SeenRanbu = count;
        }

        // the local player called the Ranbu off
        internal static void StopRanbu(Player player)
        {
            if (States.TryGetValue(player, out var s) && s.Daisho.Move != null && !s.Daisho.IsParry) CallOffDaisho(s.Daisho);
            var zdo = NViewOf(player)?.GetZDO();
            if (zdo != null) zdo.Set(DaishoRanbuStop, zdo.GetInt(DaishoRanbuCount, 0), false);
        }

        // the Ranbu was called off (a dodge, a stagger): the pose fades out from where it was
        private static void CallOffDaisho(DaishoPlay d)
        {
            d.Move = null;
            d.TailStart = Time.time;
        }

        private static void PlayDaisho(Player player, State s, DaishoMove move, bool parry)
        {
            var d = s.Daisho;
            d.Move = move;
            d.IsParry = parry;
            d.Start = Time.time;
            d.Began = Time.time;
            d.WakiOut = false;
            d.HoldUntil = -1f;
            d.NextCut = 0;
            d.Rig = RigOf(player);
            d.TailStart = -1f;
            d.HasWaki = false;
            d.Failed = false;
            s.SlashStart = -1f;   // the single cut of the other katanas does not play
            s.InSlash = false;
            if (Plugin.LogCounters.Value && player == Player.m_localPlayer)
                Plugin.Log.LogInfo($"daisho {move.Name} began on the {(d.Rig == 1 ? "female" : "male")} skeleton");
        }

        // another client's player parried or began the Ranbu, or called it off
        private static void WatchDaisho(Player player, State s)
        {
            var nview = NViewOf(player);
            if (!nview || !nview.IsValid() || nview.IsOwner()) return;
            var d = s.Daisho;
            var zdo = nview.GetZDO();
            int parry = zdo.GetInt(DaishoParryCount, 0), ranbu = zdo.GetInt(DaishoRanbuCount, 0);
            if (d.SeenParry >= 0 && parry != d.SeenParry && DaishoData.Parry != null) PlayDaisho(player, s, DaishoData.Parry, true);
            if (d.SeenRanbu >= 0 && ranbu != d.SeenRanbu && DaishoData.Ranbu != null) PlayDaisho(player, s, DaishoData.Ranbu, false);
            d.SeenParry = parry;
            d.SeenRanbu = ranbu;
            if (d.Move != null && !d.IsParry && zdo.GetInt(DaishoRanbuStop, 0) == ranbu && ranbu > 0) CallOffDaisho(d);
        }

        // seconds into the move, the hit stop's pause left out
        private static float DaishoTime(DaishoPlay d)
        {
            float t = Time.time < d.HoldUntil ? d.HoldAt : Time.time - d.Start;
            // a recording made on the clock (as long as the move takes) plays as it is, a shorter one is stretched by the tempo
            return d.IsParry || d.Move == null || d.Move.Cuts.Length < 2 || d.Move.Length >= RanbuSeconds - 0.05f ? t
                : Tempo(t, d.Move.Cuts[0].From, d.Move.Cuts[d.Move.Cuts.Length - 1].Hit, Plugin.RanbuTempo.Value, RanbuSeconds, d.Move.Length);
        }

        // how long the Ranbu takes on the clock, never shorter than its recording
        internal static float RanbuSeconds => DaishoData.Ranbu == null ? 0f : Mathf.Max(Plugin.RanbuSeconds.Value, DaishoData.Ranbu.Length);

        // the Ranbu's tempo, the clock's seconds to the recording's: the part between the first cut and the last hit is
        // stretched so the whole move takes total seconds. inside it the move starts at this share of the even speed,
        // builds up slowly and is fastest at the end, so the first cuts are slow and each next one faster
        internal static float Tempo(float t, float from, float to, float first, float total, float length)
        {
            float span = to - from, clock = total - (length - span);
            if (t <= from || span < 1e-4f || clock < 1e-4f) return t;
            if (t >= from + clock) return to + (t - from - clock);
            float s = (t - from) / clock;
            return from + span * (first * s + (1f - first) * s * s * s);
        }

        // a hit stop: the move stands still for this long, then goes on from the same point
        internal static void HoldDaisho(Player player, float seconds)
        {
            if (seconds <= 0f || !States.TryGetValue(player, out var s) || s.Daisho.Move == null) return;
            var d = s.Daisho;
            d.HoldAt = Time.time - d.Start;
            d.HoldUntil = Time.time + seconds;
            d.Start += seconds;
        }

        // whether the daisho's move is on at all for this player (the parry's timeline for the counter)
        internal static bool DaishoPlaying(Player player, bool parry) =>
            States.TryGetValue(player, out var s) && s.Daisho.Move != null && s.Daisho.IsParry == parry;

        // one frame of the timeline: watch the other clients' moves, run its events, end it. true while it plays
        private static bool DaishoFrame(Player player, State s, bool alive)
        {
            var d = s.Daisho;
            WatchDaisho(player, s);
            if (d.Move == null) return false;
            float t = DaishoTime(d);
            bool done = t >= d.Move.Length;
            if (!alive || done)
            {
                EndDaisho(player, s, alive && done);
                return false;
            }
            d.T = t;
            // an effect that fails must never take the pose of the frame with it
            try
            {
                DaishoEvents(player, s, d);
            }
            catch (System.Exception e)
            {
                if (!d.Failed) Plugin.Log.LogWarning("daisho effect failed, the move goes on: " + e);
                d.Failed = true;
            }
            return true;
        }

        private static void EndDaisho(Player player, State s, bool completed)
        {
            var d = s.Daisho;
            var move = d.Move;
            d.Move = null;
            if (d.TrailOn) d.TrailOn = false;
            if (!completed || move == null) return;
            // the sheathing takes over from where the katana ended. the legs and the body fade from the last pose
            DaishoData.KatanaEnd(move, d.Rig, out var grip, out var rot);
            s.LastPose = new Moves.Pose { Grip = grip, Rot = rot, Twist = 0f, Lean = 10f, Left = 0f, Along = Plugin.StanceLeftHand.Value, Hip = 0f };
            s.HasLast = true;
            s.Weight = 1f;
            d.TailStart = Time.time;
            StartSheathe(player, s, DaishoHold);
            if (Plugin.LogCounters.Value && player == Player.m_localPlayer) Plugin.Log.LogInfo($"daisho {move.Name} ended, the sheathing follows");
        }

        // the move's events by its timeline, the same on every client: the parry's two arcs and second hit, the Ranbu's
        // crescents, one per cut at its hit
        private static void DaishoEvents(Player player, State s, DaishoPlay d)
        {
            var move = d.Move;
            bool local = player == Player.m_localPlayer;
            var visual = AnimatorOf(player).transform;
            if (d.IsParry)
            {
                if (d.NextCut == 0 && d.T >= Daisho.ParryKatanaAt)
                {
                    d.NextCut = 1;
                    KatanaFx.SlashArc(player.transform, 2);
                    ParryWave(player, visual, local, false);
                    if (Plugin.LogCounters.Value && local) Plugin.Log.LogInfo("daisho parry: the katana's arc");
                }
                else if (d.NextCut == 1 && d.T >= Daisho.ParryWakizashiAt)
                {
                    d.NextCut = 2;
                    // leaning into the katana's arc, so the two cross as an X from behind the player
                    KatanaFx.SlashArc(player.transform, 3, true, false, ParryWakizashiRoll, 2.4f, 150f);
                    ParryWave(player, visual, local, true);
                    if (local) Counter.WakizashiBeat(player);
                    if (Plugin.LogCounters.Value && local) Plugin.Log.LogInfo("daisho parry: the wakizashi's arc");
                }
                return;
            }
            while (d.NextCut < move.Cuts.Length && d.T >= move.Cuts[d.NextCut].Hit)
            {
                var cut = move.Cuts[d.NextCut++];
                // the cut's whoosh first, so a crescent that fails does not take it with it
                KatanaFx.RanbuSound(visual.position + player.transform.rotation * Vector3.up, cut.Wakizashi ? 3 : 2);
                // and its blink of light along the blade's path, on the owner's screen
                if (local && cut.Shape != null && cut.Shape.Length > 1)
                {
                    Vector3 first = cut.Shape[0], last = cut.Shape[cut.Shape.Length - 1];
                    KatanaFx.CutFlash(visual.position + player.transform.rotation * ((first + last) * 0.5f), player.transform.rotation * (last - first), cut.Wakizashi ? 3 : 2);
                }
                if (Plugin.LogCounters.Value && local) Plugin.Log.LogInfo($"ranbu cut {d.NextCut} of {move.Cuts.Length}: {(cut.Wakizashi ? "wakizashi" : "katana")}, {Crescent.AngleOf(cut.Shape) * Mathf.Rad2Deg:0} degrees");
                var group = new Crescent.Group
                {
                    Owner = local ? player : null, Local = local, Wakizashi = cut.Wakizashi,
                    DamageShare = cut.Wakizashi ? Plugin.CrescentWakizashiShare.Value : Plugin.CrescentKatanaShare.Value,
                };
                Crescent.Spawn(cut.Shape, visual.position, player.transform.rotation, cut.Wakizashi ? 3 : 2, group,
                    d.NextCut > move.Cuts.Length - 2 ? Crescent.FinisherScale : 1f);
                // the finisher's X lands like the other cuts, with its blink and its waves (the ink frame and the crash it had
                // were the parry's, and the owner did not want them here)
            }
        }

        private const float ParryWakizashiRoll = 38f; // degrees the wakizashi's parry arc leans

        // the parry's two cuts send the Ranbu's flying waves too (the owner, 15 Nov), each at its arc's angle: the
        // katana's level, the wakizashi's leaning. they cut what they pass for a share of a Ranbu wave
        private static void ParryWave(Player player, Transform visual, bool local, bool wakizashi)
        {
            float angle = (wakizashi ? ParryWakizashiRoll : 0f) * Mathf.Deg2Rad;
            var along = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            var group = new Crescent.Group
            {
                Owner = local ? player : null, Local = local, Wakizashi = wakizashi,
                DamageShare = (wakizashi ? Plugin.CrescentWakizashiShare.Value : Plugin.CrescentKatanaShare.Value) * Plugin.ParryWaveShare.Value,
            };
            Crescent.Spawn(new[] { -along, along }, visual.position, player.transform.rotation, wakizashi ? 3 : 2, group, 1f);
        }

        // whether one of the blade's strikes is on now (the trail follows it)
        private static bool DaishoCut(DaishoPlay d, bool wakizashi)
        {
            if (d.Move == null) return false;
            foreach (var cut in d.Move.Cuts)
                if (cut.Wakizashi == wakizashi && d.T >= cut.From && d.T <= cut.To) return true;
            return false;
        }

        // the move's pose over the stance's: every bone it holds, from the packed frames, in place of the solved one
        private static void DaishoPose(Player player, State s, Quaternion[] full)
        {
            var d = s.Daisho;
            DaishoData.Sample(d.Move, d.Rig, d.T, DaishoBones, out var hips, out var waki, out var wakiRot, out var hold, out var saya, out var sayaRot);
            // the Ranbu rises into its first pose from the pose shown (the owner, 15 Nov: it jumped there)
            float ease = d.IsParry ? 1f : Smooth((Time.time - d.Began) / DaishoEaseIn);
            for (int i = 0; i < DaishoData.BoneCount; i++)
            {
                var bone = ease >= 1f ? DaishoBones[i] : QMath.Nlerp(full[DaishoMap[i]], DaishoBones[i], ease);
                full[DaishoMap[i]] = bone;
                d.Last[i] = bone;
            }
            if (ease < 1f) hips = Vector3.Lerp(s.Hips.localPosition, hips, ease);
            s.Hips.localPosition = hips;
            d.LastHips = hips;
            d.WakiPos = waki;
            d.WakiRot = wakiRot;
            d.SayaPos = saya;
            d.SayaRot = sayaRot;
            d.Hold = hold * ease;
            d.HasWaki = true;
            // back in its scabbard: the same click as the katana's
            float apart = (waki - saya).magnitude;
            if (apart > WakiOutAt) d.WakiOut = true;
            else if (d.WakiOut && apart < WakiBackAt)
            {
                d.WakiOut = false;
                ImpactFrame.Click(AnimatorOf(player).transform.position + QMath.Rotate(player.transform.rotation, saya + DaishoHipIn));
            }
        }

        // after the move: the last pose it showed fades into the sheathing's, the upper body first, the legs a little later
        private static void DaishoTail(State s, Quaternion[] full)
        {
            var d = s.Daisho;
            if (d.TailStart < 0f) return;
            float k = Time.time - d.TailStart;
            float upper = 1f - Smooth(k / DaishoTailUpper), legs = 1f - Smooth(k / DaishoTailLegs);
            if (legs <= 0f)
            {
                d.TailStart = -1f;
                d.HasWaki = false;
                return;
            }
            for (int i = 0; i < DaishoData.BoneCount; i++)
            {
                float w = i < DaishoLegs ? legs : upper;
                if (w > 0f) full[DaishoMap[i]] = QMath.Nlerp(full[DaishoMap[i]], d.Last[i], w);
            }
            s.Hips.localPosition = Vector3.Lerp(s.Hips.localPosition, d.LastHips, legs);
        }

        private static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        // the wakizashi and its scabbard, on every player holding the Thunderblood Katana: at the right hip, drawn into the
        // left hand and cut with in the moves, put back. the recorded place, taken into the left hand's own frame while it holds it.
        // it stays at the hip while the katana is put away at the other (the owner, 15 Nov: it vanished)
        private static void UpdateWaki(Player player, State s, bool katana, int back)
        {
            var d = s.Daisho;
            bool worn = katana ? RightItem(VisOf(player)) == ThunderbloodHash : back == ThunderbloodHash && Plugin.HolsterOn.Value;
            bool thunder = worn && Plugin.StanceEnabled.Value && !player.IsDead() && Plugin.DaishoOn.Value;
            if (!thunder)
            {
                HideWaki(d);
                return;
            }
            var parent = AnimatorOf(player).transform;
            if (!d.Saya || !d.Blade)
            {
                var mesh = KatanaModel.WakizashiMesh();
                var sayaMesh = KatanaModel.WakizashiSayaMesh();
                var material = KatanaModel.WakizashiMaterial();
                if (!mesh || !sayaMesh || !material) return;
                d.Saya = new GameObject("IaidoKatana_wakizashi_saya", typeof(MeshFilter), typeof(MeshRenderer)) { layer = parent.gameObject.layer };
                d.Blade = new GameObject("IaidoKatana_wakizashi", typeof(MeshFilter), typeof(MeshRenderer)) { layer = parent.gameObject.layer };
                foreach (var go in new[] { d.Saya, d.Blade })
                {
                    go.transform.SetParent(parent, false);
                    go.GetComponent<MeshRenderer>().sharedMaterial = material;
                }
                d.Saya.GetComponent<MeshFilter>().sharedMesh = sayaMesh;
                d.Blade.GetComponent<MeshFilter>().sharedMesh = mesh;
                KatanaFx.FrostFlames(d.Blade.transform, KatanaModel.WakizashiShape());
            }
            d.Saya.SetActive(true);
            d.Blade.SetActive(true);
            var root = player.transform.rotation;
            var node = KatanaModel.NodeRotation;
            Vector3 sayaAt, wakiAt;
            Quaternion sayaTurn, wakiTurn;
            if (d.HasWaki && (d.Move != null || d.TailStart >= 0f))
            {
                // the recorded frames (the player's frame, feet origin): the scabbard follows the hips, the blade is drawn
                sayaAt = parent.position + QMath.Rotate(root, d.SayaPos + DaishoHipIn);
                sayaTurn = QMath.Mul(root, d.SayaRot);
                wakiAt = parent.position + QMath.Rotate(root, d.WakiPos + DaishoHipIn);
                wakiTurn = QMath.Mul(root, d.WakiRot);
                // held in the left hand: the hand's own frame, so the grip is where the fingers are whatever the pose
                float hold = Mathf.Clamp01(d.Hold);
                if (hold > 0.001f && s.Bones != null && s.Bones[LHand])
                {
                    var hand = s.Bones[LHand];
                    var inHandAt = hand.position + hand.rotation * DaishoHandOffset;
                    var inHandTurn = QMath.Mul(hand.rotation, DaishoHandTurn);
                    wakiAt = Vector3.Lerp(wakiAt, inHandAt, hold);
                    wakiTurn = QMath.Nlerp(wakiTurn, inHandTurn, hold);
                }
            }
            else
            {
                // at the hip while no move plays, in the frame carried with the hips like the katana's scabbard
                d.Rig = RigOf(player);
                var restTurn = DaishoData.Parry != null ? RestSayaTurn(d.Rig) : Quaternion.identity;
                var origin = s.Hips.position - QMath.Rotate(root, StanceSolver.RestHips);
                sayaAt = wakiAt = origin + QMath.Rotate(root, DaishoHipPosition + DaishoHipIn);
                sayaTurn = wakiTurn = QMath.Mul(root, restTurn);
            }
            float scale = 1f / Mathf.Max(1e-4f, parent.lossyScale.x);
            Place(d.Saya.transform, sayaAt, sayaTurn, node, scale);
            Place(d.Blade.transform, wakiAt, wakiTurn, node, scale);
            // the wakizashi's own trail through its cuts
            d.Trail = d.Trail ?? BladeTrail.Make(player.transform, d.Blade.layer);
            d.TrailOn = d.Move != null && DaishoCut(d, true) && Plugin.SlashTrailOn.Value;
            d.Trail?.Sample(d.Blade.transform, d.TrailOn, 3);
        }

        // the scabbard's turn when no move plays, from the packed pose's first frame
        private static Quaternion RestSayaTurn(int rig)
        {
            var r = DaishoData.Parry.Rigs[rig];
            return new Quaternion(r.Saya[3], r.Saya[4], r.Saya[5], r.Saya[6]);
        }

        private static void Place(Transform t, Vector3 attachAt, Quaternion attachTurn, Quaternion node, float scale)
        {
            t.position = attachAt + QMath.Rotate(attachTurn, DaishoNodePosition);
            t.rotation = QMath.Mul(attachTurn, node);
            t.localScale = Vector3.one * scale;
        }

        private static void HideWaki(DaishoPlay d)
        {
            if (d.Saya && d.Saya.activeSelf) d.Saya.SetActive(false);
            if (d.Blade && d.Blade.activeSelf) d.Blade.SetActive(false);
            d.Trail?.Clear();
        }
    }
}
