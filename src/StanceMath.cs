using System;
using UnityEngine;

namespace Iaido_Katana
{
    // rotation math in plain c#. unity's own rotation helpers only run inside the engine, so the stance solver uses
    // these instead and gives the same result in the game and in the offline preview
    internal static class QMath
    {
        internal static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

        internal static Vector3 Cross(Vector3 a, Vector3 b) =>
            new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);

        internal static float Length(Vector3 v) => (float)Math.Sqrt(Dot(v, v));

        internal static Vector3 Norm(Vector3 v)
        {
            float l = Length(v);
            return l > 1e-8f ? new Vector3(v.x / l, v.y / l, v.z / l) : Vector3.zero;
        }

        internal static Quaternion Mul(Quaternion a, Quaternion b) => new Quaternion(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);

        internal static Quaternion Conj(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, q.w);

        internal static Quaternion Normalize(Quaternion q)
        {
            float l = (float)Math.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return l > 1e-8f ? new Quaternion(q.x / l, q.y / l, q.z / l, q.w / l) : new Quaternion(0f, 0f, 0f, 1f);
        }

        internal static Vector3 Rotate(Quaternion q, Vector3 v)
        {
            var u = new Vector3(q.x, q.y, q.z);
            var t = Cross(u, v) * 2f;
            return v + t * q.w + Cross(u, t);
        }

        // the shortest rotation that turns one direction into another
        internal static Quaternion FromTo(Vector3 from, Vector3 to)
        {
            var a = Norm(from);
            var b = Norm(to);
            float d = Dot(a, b);
            if (d < -0.99999f)
            {
                var axis = Cross(new Vector3(1f, 0f, 0f), a);
                if (Length(axis) < 1e-4f) axis = Cross(new Vector3(0f, 1f, 0f), a);
                axis = Norm(axis);
                return new Quaternion(axis.x, axis.y, axis.z, 0f);
            }
            var c = Cross(a, b);
            return Normalize(new Quaternion(c.x, c.y, c.z, 1f + d));
        }

        // like unity's LookRotation: +z along forward, +y as close to up as it can be
        internal static Quaternion Look(Vector3 forward, Vector3 up)
        {
            var z = Norm(forward);
            var x = Norm(Cross(up, z));
            var y = Cross(z, x);
            float m00 = x.x, m11 = y.y, m22 = z.z, trace = m00 + m11 + m22;
            if (trace > 0f)
            {
                float s = (float)Math.Sqrt(trace + 1f) * 2f;
                return Normalize(new Quaternion((y.z - z.y) / s, (z.x - x.z) / s, (x.y - y.x) / s, 0.25f * s));
            }
            if (m00 > m11 && m00 > m22)
            {
                float s = (float)Math.Sqrt(1f + m00 - m11 - m22) * 2f;
                return Normalize(new Quaternion(0.25f * s, (y.x + x.y) / s, (z.x + x.z) / s, (y.z - z.y) / s));
            }
            if (m11 > m22)
            {
                float s = (float)Math.Sqrt(1f + m11 - m00 - m22) * 2f;
                return Normalize(new Quaternion((y.x + x.y) / s, 0.25f * s, (z.y + y.z) / s, (z.x - x.z) / s));
            }
            float s2 = (float)Math.Sqrt(1f + m22 - m00 - m11) * 2f;
            return Normalize(new Quaternion((z.x + x.z) / s2, (z.y + y.z) / s2, 0.25f * s2, (x.y - y.x) / s2));
        }

        // angles in degrees, applied like unity's Quaternion.Euler: z, then x, then y
        internal static Quaternion Euler(float x, float y, float z)
        {
            Quaternion Axis(float deg, Vector3 axis)
            {
                double half = deg * Math.PI / 360.0;
                float s = (float)Math.Sin(half);
                return new Quaternion(axis.x * s, axis.y * s, axis.z * s, (float)Math.Cos(half));
            }
            return Mul(Axis(y, new Vector3(0f, 1f, 0f)), Mul(Axis(x, new Vector3(1f, 0f, 0f)), Axis(z, new Vector3(0f, 0f, 1f))));
        }

        internal static Quaternion Nlerp(Quaternion a, Quaternion b, float t)
        {
            if (a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w < 0f) b = new Quaternion(-b.x, -b.y, -b.z, -b.w);
            return Normalize(new Quaternion(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t, a.w + (b.w - a.w) * t));
        }
    }

    // one arm in world space: where its joints are and how its three bones are turned
    internal struct Arm
    {
        internal Vector3 Shoulder, Elbow, Wrist;
        internal Quaternion Upper, Lower, Hand;
    }

    // the iai stance, in the player's own frame: x to the right, y up, z forward, meters from the feet
    internal sealed class StanceSettings
    {
        internal float SpineTwist = -33f;   // the upper body turns left, which brings the right shoulder forward
        internal float SpineLean = 8f;      // and leans forward a little
        internal bool HeadForward = true;   // the neck turns back, so the head keeps looking where the player looks
        // the right hand on the grip in front of the belly, the elbow bent and hanging down the side, the forearm across
        // the belly and the wrist straight (found by StancePreview tune, the user's drawing of 11 Oct)
        internal Vector3 Grip = new Vector3(-0.05f, 1.12f, 0.38f);
        internal float BladeYaw = 215f;     // the blade points back and out past the left hip (180 is straight back, which runs through a cape)
        internal float BladePitch = 26f;    // and slopes down
        internal float EdgeRoll = -104f;    // the edge turned out from up, so the right hand lies on the grip palm down with a straight wrist
        internal float LeftAlongBlade = 0.3f; // the left hand on the scabbard this far from the grip, the arm hanging relaxed
        internal float LeftOnBlade = 1f;      // 1 the left hand is on the blade, 0 it is at LeftHand (pulled back to the hip in the slash)
        internal Vector3 LeftHand = new Vector3(-0.24f, 1.0f, -0.04f);
        internal Vector3 RightElbow = new Vector3(0.37f, 1.17f, 0.2f); // elbows bend toward these points
        internal Vector3 LeftElbow = new Vector3(-0.8f, 1.19f, -0.12f);
        internal float RightNatural = 0.8f; // 1: the elbows point wherever the arm is most natural, less: pulled toward these poles
        internal float LeftNatural = 0.8f;
        internal Quaternion? Attach;          // set by the moves: the blade's turn in the player's frame, in place of yaw, pitch and roll
        internal Quaternion? SheathAt;        // where the scabbard really hangs (the blade's turn and SheathGrip), when the game knows it.
        internal Vector3 SheathGrip;          // the sheathing slides the blade into it, not into the stance's own aim
        internal Vector3 MouthAxis;           // the scabbard's way from the mouth to the tip, where the left hand holds it off the blade (zero: the blade's)
    }

    // the thunderclap's low crouch, in the player's own frame, meters from the feet
    internal sealed class CrouchSettings
    {
        internal float HipDrop = 0.4f;      // the hips sink this far
        internal float HipBack = 0.04f;     // and move back over the heels a little
        internal float HipPitch = 22f;      // the pelvis tips forward
        internal float HipTurn = -15f;      // and turns left with the stance
        internal Vector3 RightFoot = new Vector3(0.16f, 0f, 0.42f);  // the front foot on the ground
        internal Vector3 LeftFoot = new Vector3(-0.22f, 0f, -0.48f); // the back foot
        internal float RightFootTurn = 15f; // toes out
        internal float LeftFootTurn = -55f;
        internal Vector3 RightKnee = new Vector3(0.25f, 0.6f, 1.2f); // the knees bend toward these
        internal Vector3 LeftKnee = new Vector3(-0.35f, 0f, 0.3f);

        // a footing between two others, written into into
        internal static void Lerp(CrouchSettings a, CrouchSettings b, float t, CrouchSettings into)
        {
            into.HipDrop = a.HipDrop + (b.HipDrop - a.HipDrop) * t;
            into.HipBack = a.HipBack + (b.HipBack - a.HipBack) * t;
            into.HipPitch = a.HipPitch + (b.HipPitch - a.HipPitch) * t;
            into.HipTurn = a.HipTurn + (b.HipTurn - a.HipTurn) * t;
            into.RightFoot = a.RightFoot + (b.RightFoot - a.RightFoot) * t;
            into.LeftFoot = a.LeftFoot + (b.LeftFoot - a.LeftFoot) * t;
            into.RightFootTurn = a.RightFootTurn + (b.RightFootTurn - a.RightFootTurn) * t;
            into.LeftFootTurn = a.LeftFootTurn + (b.LeftFootTurn - a.LeftFootTurn) * t;
            into.RightKnee = a.RightKnee + (b.RightKnee - a.RightKnee) * t;
            into.LeftKnee = a.LeftKnee + (b.LeftKnee - a.LeftKnee) * t;
        }
    }

    internal static class StanceSolver
    {
        internal const float PalmReach = 0.07f; // from the wrist to the middle of the palm, meters
        internal const float KatanaLength = 1.815f; // from the grip to the tip (2.145 m, held 0.33 m from the pommel)
        internal const float AnkleHeight = 0.125f; // the ankle bone above the ground, in the ripped player at rest

        // the hips sunk, moved back and tipped for the crouch, weight 0 leaves them as animated
        internal static void Hips(Quaternion rootRot, CrouchSettings c, float weight, ref Vector3 pos, ref Quaternion rot)
        {
            pos = pos + QMath.Rotate(rootRot, new Vector3(0f, -c.HipDrop, -c.HipBack)) * weight;
            rot = QMath.Mul(Turn(rootRot, QMath.Euler(c.HipPitch * weight, c.HipTurn * weight, 0f)), rot);
        }

        // a rotation in the player's own frame, as a world rotation
        internal static Quaternion Turn(Quaternion rootRot, Quaternion local) => QMath.Mul(rootRot, QMath.Mul(local, QMath.Conj(rootRot)));

        // one leg (thigh, shin, foot, after the hips moved) toward its crouch foot. animated is the same leg before the
        // hips moved: weight 0 keeps its ankle, knee and foot, 1 puts the ankle above the crouch foot. the foot keeps its
        // animated tilt, so it stays flat, turned out by footTurn
        internal static void Leg(ref Arm leg, Arm animated, Vector3 rootPos, Quaternion rootRot, Vector3 foot, float footTurn, Vector3 knee, float weight)
        {
            var crouched = rootPos + QMath.Rotate(rootRot, new Vector3(foot.x, foot.y + AnkleHeight, foot.z));
            var ankle = animated.Wrist + (crouched - animated.Wrist) * weight;
            var pole = animated.Elbow + (rootPos + QMath.Rotate(rootRot, knee) - animated.Elbow) * weight;
            TwoBone(ref leg, ankle, pole);
            leg.Hand = QMath.Mul(Turn(rootRot, QMath.Euler(0f, footTurn * weight, 0f)), animated.Hand);
        }
        // the hips of the ripped player at rest. the stance is measured from the hips, so it follows the body when
        // the animation bobs or crouches
        internal static readonly Vector3 RestHips = new Vector3(0f, 0.91f, -0.007f);

        internal static Vector3 LeftTarget(Vector3 rootPos, Quaternion rootRot, Vector3 grip, Vector3 blade, StanceSettings s)
        {
            var onBlade = grip + blade * s.LeftAlongBlade;
            if (s.LeftOnBlade >= 1f) return onBlade;
            var atHip = rootPos + QMath.Rotate(rootRot, s.LeftHand);
            return atHip + (onBlade - atHip) * Mathf.Clamp01(s.LeftOnBlade);
        }

        // the player's arms at rest, from the game's skeleton (art\out\rig.txt): the elbow bends forward about the bones'
        // own z (the left arm's about -z), the hand's rest turn on the forearm, the forearm's way seen from the hand at
        // rest, and the hand's thumb side (a grip runs through the fist this way)
        internal sealed class ArmRig
        {
            internal Vector3 Hinge;
            internal Quaternion RestHand;
            internal Vector3 Forearm;
            internal Vector3 Thumb;
        }

        internal static readonly ArmRig RightRig = new ArmRig
        {
            Hinge = new Vector3(0f, 0f, 1f), RestHand = new Quaternion(-0.06456f, -0.028324f, -0.012623f, 0.997432f),
            Forearm = new Vector3(-0.021524f, 0.991346f, 0.129503f), Thumb = new Vector3(-1f, 0f, 0f),
        };

        internal static readonly ArmRig LeftRig = new ArmRig
        {
            Hinge = new Vector3(0f, 0f, -1f), RestHand = new Quaternion(-0.06456f, 0.028324f, 0.012623f, 0.997432f),
            Forearm = new Vector3(0.021524f, 0.991346f, 0.129503f), Thumb = new Vector3(1f, 0f, 0f),
        };

        // the forearm's share of the hand's roll (turning the palm up or down), the wrist takes the rest, like a humanoid
        // avatar's default lower arm twist. without it the whole roll twisted the wrist
        internal const float ForearmRoll = 0.5f;

        // where the elbow goes for a wrist at target, bent toward the pole, the wrist (pulled in to the arm's reach), and
        // the way the elbow points out from the line between shoulder and wrist
        internal static void Elbow(Arm arm, Vector3 target, Vector3 pole, out Vector3 elbow, out Vector3 wrist, out Vector3 bend)
        {
            float upper = QMath.Length(arm.Elbow - arm.Shoulder);
            float lower = QMath.Length(arm.Wrist - arm.Elbow);
            var toTarget = target - arm.Shoulder;
            float d = Mathf.Clamp(QMath.Length(toTarget), 0.01f, upper + lower - 0.0005f);
            var dir = QMath.Norm(toTarget);
            var toPole = pole - arm.Shoulder;
            bend = toPole - dir * QMath.Dot(toPole, dir);
            if (QMath.Length(bend) < 1e-5f) bend = arm.Elbow - arm.Shoulder - dir * QMath.Dot(arm.Elbow - arm.Shoulder, dir);
            bend = QMath.Norm(bend);
            float cos = Mathf.Clamp((upper * upper + d * d - lower * lower) / (2f * upper * d), -1f, 1f);
            float sin = (float)Math.Sqrt(1f - cos * cos);
            elbow = arm.Shoulder + (dir * cos + bend * sin) * upper;
            wrist = arm.Shoulder + dir * d;
        }

        // one arm to the wrist target with its hand turned to hand. the elbow bends toward the pole about its own axis
        // only, like a real elbow (the upper arm rolls to point it there), the forearm takes its share of the hand's roll,
        // and the wrist keeps the rest and its bend
        internal static void Reach(ref Arm arm, Vector3 target, Vector3 pole, Quaternion hand, ArmRig rig)
        {
            Elbow(arm, target, pole, out var elbow, out var wrist, out var bend);
            var hinge = QMath.Norm(QMath.Cross(bend, QMath.Norm(wrist - arm.Shoulder)));
            arm.Upper = Along(QMath.Norm(elbow - arm.Shoulder), hinge, rig);
            var forearm = Along(QMath.Norm(wrist - elbow), hinge, rig);
            arm.Lower = QMath.Mul(forearm, AxisAngle(new Vector3(0f, 1f, 0f), Roll(forearm, hand, rig) * ForearmRoll));
            arm.Hand = hand;
            arm.Elbow = elbow;
            arm.Wrist = wrist;
        }

        // how far (degrees) the hand is rolled about the forearm from its rest turn, for a forearm turned as forearm
        internal static float Roll(Quaternion forearm, Quaternion hand, ArmRig rig)
        {
            var local = QMath.Mul(QMath.Mul(QMath.Conj(forearm), hand), QMath.Conj(rig.RestHand));
            if (local.w < 0f) local = new Quaternion(-local.x, -local.y, -local.z, -local.w);
            return 2f * (float)Math.Atan2(local.y, local.w) * Mathf.Rad2Deg;
        }

        // an arm bone's world turn with its length (+y) along way and the elbow's axis on hinge, not rolled
        internal static Quaternion Along(Vector3 way, Vector3 hinge, ArmRig rig) =>
            QMath.Mul(Frame(way, hinge), QMath.Conj(Frame(new Vector3(0f, 1f, 0f), rig.Hinge)));

        // the turn whose +y is y and whose +z is z made square to it
        private static Quaternion Frame(Vector3 y, Vector3 z) => QMath.Look(QMath.Norm(z - y * QMath.Dot(z, y)), y);

        // a hand turned so its thumb side points along thumb and its forearm comes in as close to forearm as it can:
        // the left hand round the scabbard or the handle, the grip through the fist
        internal static Quaternion Holding(Vector3 thumb, Vector3 forearm, ArmRig rig) =>
            QMath.Mul(QMath.Look(thumb, forearm), QMath.Conj(QMath.Look(rig.Thumb, rig.Forearm)));

        // how a hand sits on its forearm, degrees from the rest pose: bent toward the palm (flex) and toward the thumb
        // (thumb), the other way negative, and rolled about the forearm (palm down or up). fore is the forearm not
        // rolled (Along), way its direction
        internal static void Wrist(Quaternion fore, Vector3 way, Quaternion hand, ArmRig rig, out float flex, out float thumb, out float roll)
        {
            var f = QMath.Rotate(QMath.Conj(hand), way);
            flex = Wrap((float)(Math.Atan2(-f.z, f.y) - Math.Atan2(-rig.Forearm.z, rig.Forearm.y)) * Mathf.Rad2Deg);
            thumb = Wrap((float)(Math.Atan2(-QMath.Dot(f, rig.Thumb), f.y) - Math.Atan2(-QMath.Dot(rig.Forearm, rig.Thumb), rig.Forearm.y)) * Mathf.Rad2Deg);
            roll = Roll(fore, hand, rig);
        }

        // how far a point is from the line between a and b
        private static float Distance(Vector3 point, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float u = Mathf.Clamp01(QMath.Dot(point - a, ab) / Math.Max(1e-6f, QMath.Dot(ab, ab)));
            return QMath.Length(point - (a + ab * u));
        }

        private static float Wrap(float degrees) => degrees > 180f ? degrees - 360f : degrees < -180f ? degrees + 360f : degrees;

        // how strained a wrist is: a little inside the comfortable ranges, steeply more outside them
        internal static float Strain(float flex, float thumb, float roll)
        {
            float Over(float v, float low, float high) => Math.Max(0f, Math.Max(low - v, v - high));
            float Sq(float x) => x * x;
            return Sq(flex / 45f) + Sq(thumb / 25f) + 0.5f * Sq(roll / 90f)
                   + 4f * (Sq(Over(flex, -40f, 40f) / 10f) + Sq(Over(thumb, -25f, 15f) / 10f) + Sq(Over(roll, -90f, 90f) / 10f));
        }

        private const float PolePull = 2f; // how strongly the stance's pole holds the elbow, at natural 0

        // the body round the spine, to keep the elbows out of it: an upright ellipse 0.18 m to each side and 0.14 m to
        // the front and back, turned and leant with the chest, 0.85 to 1.6 m above the feet (measured from the hips)
        internal struct Torso
        {
            internal Vector3 Feet;
            internal Quaternion Root, Chest;

            internal Torso(Vector3 rootPos, Quaternion rootRot, StanceSettings s)
            {
                Feet = rootPos;
                Root = rootRot;
                Chest = QMath.Euler(s.SpineLean, s.SpineTwist, 0f);
            }

            // how deep a point is inside the body with room meters around it, 0 outside to 1 at the spine
            internal float Inside(Vector3 point, float room)
            {
                if (Root.w == 0f && Root.x == 0f && Root.y == 0f && Root.z == 0f) return 0f;
                var local = QMath.Rotate(QMath.Conj(Root), point - Feet);
                if (local.y < 0.85f || local.y > 1.6f) return 0f;
                var c = QMath.Rotate(QMath.Conj(Chest), new Vector3(local.x, local.y - 1f, local.z + 0.005f));
                float x = c.x / (0.18f + room), z = c.z / (0.14f + room);
                return Math.Max(0f, 1f - x * x - z * z);
            }
        }

        // where each elbow pointed last frame (in the player's frame) and how far it may turn this frame, degrees. two
        // ways can be about as natural, and without this the elbow could jump from one to the other in a frame
        internal sealed class ElbowMemory
        {
            internal Vector3 Right, Left;
            internal bool Has;
            internal float MaxTurn;
        }

        // the elbow's way turned from last frame's toward want by at most MaxTurn degrees about the arm's line
        private static Vector3 Limit(Vector3 want, Vector3 dir, Quaternion rootRot, ElbowMemory memory, ref Vector3 last)
        {
            if (memory == null) return want;
            var wantLocal = QMath.Rotate(QMath.Conj(rootRot), want);
            var line = QMath.Rotate(QMath.Conj(rootRot), dir);
            var was = last - line * QMath.Dot(last, line);
            if (memory.Has && memory.MaxTurn > 0f && QMath.Length(was) > 1e-4f)
            {
                was = QMath.Norm(was);
                float angle = (float)Math.Atan2(QMath.Dot(QMath.Cross(was, wantLocal), line), QMath.Dot(was, wantLocal)) * Mathf.Rad2Deg;
                wantLocal = QMath.Rotate(AxisAngle(line, Mathf.Clamp(angle, -memory.MaxTurn, memory.MaxTurn)), was);
            }
            last = wantLocal;
            return QMath.Rotate(rootRot, wantLocal);
        }

        // which way the elbow points out from the line between shoulder and wrist. a nearly straight arm can point it
        // anywhere, and that sets how far the forearm must roll, so it is tried all round and the way where the wrist
        // bends and rolls least wins, pulled toward the pole by 1 - natural (and a little always, to settle ties)
        // holding: the hand holds the scabbard or the handle with its thumb along this way, and turns about it with the
        // forearm (zero: the hand stays as hand)
        // keepFrom to keepTo: a line the forearm stays 8 cm away from (the left arm from the blade), none when equal
        internal static Vector3 Swivel(Arm arm, Vector3 target, Quaternion hand, Vector3 pole, ArmRig rig, float natural, Torso body,
            Vector3 holding = default, Vector3 keepFrom = default, Vector3 keepTo = default)
        {
            float upper = QMath.Length(arm.Elbow - arm.Shoulder);
            float lower = QMath.Length(arm.Wrist - arm.Elbow);
            var toTarget = target - arm.Shoulder;
            float d = Mathf.Clamp(QMath.Length(toTarget), 0.01f, upper + lower - 0.0005f);
            var dir = QMath.Norm(toTarget);
            float cos = Mathf.Clamp((upper * upper + d * d - lower * lower) / (2f * upper * d), -1f, 1f);
            float sin = (float)Math.Sqrt(1f - cos * cos);
            var wrist = arm.Shoulder + dir * d;
            var e1 = QMath.Norm(QMath.Cross(dir, Math.Abs(dir.y) < 0.9f ? new Vector3(0f, 1f, 0f) : new Vector3(1f, 0f, 0f)));
            var e2 = QMath.Cross(dir, e1);
            var toPole = pole - arm.Shoulder;
            var poleBend = toPole - dir * QMath.Dot(toPole, dir);
            poleBend = QMath.Length(poleBend) > 1e-5f ? QMath.Norm(poleBend) : e1;
            float pull = (1f - Mathf.Clamp01(natural)) * PolePull + 0.05f;

            float Cost(float angle)
            {
                var bend = e1 * (float)Math.Cos(angle) + e2 * (float)Math.Sin(angle);
                var elbow = arm.Shoulder + (dir * cos + bend * sin) * upper;
                var way = QMath.Norm(wrist - elbow);
                var turned = holding == Vector3.zero ? hand : Holding(holding, way, rig);
                Wrist(Along(way, QMath.Norm(QMath.Cross(bend, dir)), rig), way, turned, rig, out float flex, out float thumb, out float roll);
                // the elbow and the forearm stay out of the body, and off the line (the hand end holds it, so it may touch)
                float inside = body.Inside(elbow, 0.07f) + body.Inside((elbow + wrist) * 0.5f, 0.06f);
                if (keepFrom != keepTo)
                    for (float u = 0f; u < 0.7f; u += 0.3f)
                    {
                        float near = Math.Max(0f, 1f - Distance(elbow + (wrist - elbow) * u, keepFrom, keepTo) / 0.08f);
                        inside += near * near;
                    }
                return Strain(flex, thumb, roll) + pull * (1f - QMath.Dot(bend, poleBend)) + 20f * inside;
            }

            // all round in 15 degree steps, then finer round the best
            float best = 0f, bestCost = float.MaxValue;
            for (int k = 0; k < 24; k++)
            {
                float angle = k * Mathf.PI / 12f;
                float c = Cost(angle);
                if (c < bestCost)
                {
                    bestCost = c;
                    best = angle;
                }
            }
            for (float step = Mathf.PI / 24f; step > 0.004f; step *= 0.5f)
                foreach (float angle in new[] { best - step, best + step })
                {
                    float c = Cost(angle);
                    if (c < bestCost)
                    {
                        bestCost = c;
                        best = angle;
                    }
                }
            return e1 * (float)Math.Cos(best) + e2 * (float)Math.Sin(best);
        }

        // turns a two-bone chain so the wrist reaches the target, bending toward the pole. the bones keep their twist
        internal static void TwoBone(ref Arm arm, Vector3 target, Vector3 pole)
        {
            float upper = QMath.Length(arm.Elbow - arm.Shoulder);
            float lower = QMath.Length(arm.Wrist - arm.Elbow);
            var toTarget = target - arm.Shoulder;
            float d = Mathf.Clamp(QMath.Length(toTarget), 0.01f, upper + lower - 0.0005f);
            var dir = QMath.Norm(toTarget);
            var toPole = pole - arm.Shoulder;
            var bend = toPole - dir * QMath.Dot(toPole, dir);
            if (QMath.Length(bend) < 1e-5f) bend = arm.Elbow - arm.Shoulder - dir * QMath.Dot(arm.Elbow - arm.Shoulder, dir);
            bend = QMath.Norm(bend);
            float cos = Mathf.Clamp((upper * upper + d * d - lower * lower) / (2f * upper * d), -1f, 1f);
            float sin = (float)Math.Sqrt(1f - cos * cos);
            var elbow = arm.Shoulder + (dir * cos + bend * sin) * upper;

            var q1 = QMath.FromTo(arm.Elbow - arm.Shoulder, elbow - arm.Shoulder);
            var wrist = elbow + QMath.Rotate(q1, arm.Wrist - arm.Elbow);
            var q2 = QMath.FromTo(wrist - elbow, arm.Shoulder + dir * d - elbow);
            var handLocal = QMath.Mul(QMath.Conj(arm.Lower), arm.Hand);
            arm.Upper = QMath.Mul(q1, arm.Upper);
            arm.Lower = QMath.Mul(q2, QMath.Mul(q1, arm.Lower));
            arm.Hand = QMath.Mul(arm.Lower, handLocal); // the hand keeps its bend at the wrist unless set after
            arm.Elbow = elbow;
            arm.Wrist = arm.Shoulder + dir * d;
        }

        // the turn of the upper body, as a world rotation for the whole spine (the game splits it over two bones)
        internal static Quaternion Spine(Quaternion rootRot, StanceSettings s) =>
            QMath.Mul(rootRot, QMath.Mul(QMath.Euler(s.SpineLean, s.SpineTwist, 0f), QMath.Conj(rootRot)));

        // half of a rotation, for spreading the spine turn over two bones
        internal static Quaternion Half(Quaternion q) => QMath.Nlerp(new Quaternion(0f, 0f, 0f, 1f), q, 0.5f);

        // where the stance puts the sword: the grip point and the attach frame (blade along +z, edge along +x)
        internal static void Sword(Vector3 rootPos, Quaternion rootRot, StanceSettings s, out Vector3 grip, out Quaternion attach, out Vector3 blade)
        {
            grip = rootPos + QMath.Rotate(rootRot, s.Grip);
            if (s.Attach.HasValue)
            {
                attach = QMath.Mul(rootRot, s.Attach.Value);
                blade = QMath.Rotate(attach, new Vector3(0f, 0f, 1f));
                return;
            }
            var up = QMath.Rotate(rootRot, new Vector3(0f, 1f, 0f));
            blade = QMath.Rotate(rootRot, QMath.Rotate(QMath.Euler(s.BladePitch, s.BladeYaw, 0f), new Vector3(0f, 0f, 1f)));
            var edge = QMath.Norm(up - blade * QMath.Dot(up, blade));
            if (s.EdgeRoll != 0f) edge = QMath.Rotate(QMath.Normalize(AxisAngle(blade, s.EdgeRoll)), edge);
            attach = QMath.Look(blade, QMath.Cross(blade, edge)); // this up makes the frame's +x the edge
        }

        private static Quaternion AxisAngle(Vector3 axis, float degrees)
        {
            double half = degrees * Math.PI / 360.0;
            float sin = (float)Math.Sin(half);
            return new Quaternion(axis.x * sin, axis.y * sin, axis.z * sin, (float)Math.Cos(half));
        }

        // both arms into the stance. attachPos and attachRot are the sword attach point's place in the right hand,
        // handScale the right hand's world scale
        internal static void Solve(Vector3 rootPos, Quaternion rootRot, StanceSettings s, ref Arm right, ref Arm left,
            Vector3 attachPos, Quaternion attachRot, float handScale, ElbowMemory memory = null)
        {
            var body = new Torso(rootPos, rootRot, s);
            Sword(rootPos, rootRot, s, out var grip, out var attach, out var blade);
            var hand = QMath.Mul(attach, QMath.Conj(attachRot));
            var fist = attachPos * handScale;
            var wrist = grip - QMath.Rotate(hand, fist);
            var rightPole = rootPos + QMath.Rotate(rootRot, s.RightElbow);
            var rightBend = Swivel(right, wrist, hand, rightPole, RightRig, s.RightNatural, body);
            if (memory != null) rightBend = Limit(rightBend, QMath.Norm(wrist - right.Shoulder), rootRot, memory, ref memory.Right);
            Reach(ref right, wrist, right.Shoulder + rightBend, hand, RightRig);

            // the left fist round the scabbard's mouth (or the blade, or the handle behind the right hand): the thumb
            // toward the guard on the scabbard, toward the tip on the handle. the hand turns about the scabbard to meet
            // its forearm, the elbow points where that arm is most natural, a few rounds to settle both
            var leftTarget = LeftTarget(rootPos, rootRot, grip, blade, s);
            var mouth = s.MouthAxis == Vector3.zero ? blade : QMath.Rotate(rootRot, s.MouthAxis);
            float on = Mathf.Clamp01(s.LeftOnBlade);
            var thumb = mouth * -(1f - on) + blade * (s.LeftAlongBlade < 0f ? on : -on);
            thumb = QMath.Length(thumb) > 0.2f ? QMath.Norm(thumb) : blade;
            var leftPole = rootPos + QMath.Rotate(rootRot, s.LeftElbow);
            Elbow(left, leftTarget, leftPole, out var leftElbow, out _, out _);
            var leftWrist = leftTarget;
            var bend = Vector3.zero;
            for (int i = 0; i < 3; i++)
            {
                leftWrist = leftTarget - QMath.Rotate(Holding(thumb, leftTarget - leftElbow, LeftRig), fist);
                bend = Swivel(left, leftWrist, left.Hand, leftPole, LeftRig, s.LeftNatural, body, thumb, grip - blade * 0.33f, grip + blade * KatanaLength);
                Elbow(left, leftWrist, left.Shoulder + bend, out leftElbow, out leftWrist, out _);
            }
            if (memory != null)
            {
                bend = Limit(bend, QMath.Norm(leftWrist - left.Shoulder), rootRot, memory, ref memory.Left);
                Elbow(left, leftWrist, left.Shoulder + bend, out leftElbow, out leftWrist, out _);
                memory.Has = true;
            }
            var leftHand = Holding(thumb, leftWrist - leftElbow, LeftRig);
            Reach(ref left, leftTarget - QMath.Rotate(leftHand, fist), left.Shoulder + bend, leftHand, LeftRig);
        }
    }

    // the iai slash on a parry: the sword is drawn from the stance and cuts across from left to right with the edge
    // leading, the left hand pulls back to the hip, and it ends held out to the right. the watchful hold, the flick and
    // the sheathing come after it (Moves.Noto). keys are stance settings over time, the arms follow through the same solver
    internal static class IaiSlash
    {
        internal const float HitTime = 0.11f;   // the cut passes the middle, the counter lands here
        internal const float Length = 0.3f;

        private struct Key
        {
            internal float Time, Yaw, Pitch, Roll, Twist, Lean, Left;
            internal Vector3 Grip;
            internal bool Stance;
        }

        // yaw keeps counting past 360 so the blade swings the short way: back left, left, front. the arm sweeps out
        // to the right and the blade ends pointing ahead: with the palm down a real wrist can not point it further
        // right. the palm stays down all through, like in the stance, so the forearm never rolls over (the user's
        // drawing of 11 Oct, found by StancePreview tune with natural arms)
        private static readonly Key[] Keys =
        {
            new Key { Time = 0f, Stance = true },
            new Key { Time = 0.05f, Grip = new Vector3(0.35f, 1.05f, 0.25f), Yaw = 310f, Pitch = -10f, Roll = -119f, Twist = -28f, Lean = 10f, Left = 0f },
            new Key { Time = 0.12f, Grip = new Vector3(0.6f, 1.05f, 0.15f), Yaw = 340f, Pitch = -5f, Roll = -115f, Twist = -8f, Lean = 12f, Left = 0f },
            Held(0.15f),
            Held(Length),
        };

        // held out to the right after the cut (zanshin), the arm a little bent and angled down
        private static Key Held(float time) => new Key
        {
            Time = time, Grip = new Vector3(0.65f, 1.06f, -0.1f), Yaw = 345f, Pitch = 3f, Roll = -133f, Twist = 1f, Lean = 12f, Left = 0f
        };

        // the pose t seconds into the slash, written into "into". stance is the player's stance settings
        internal static void Sample(float t, StanceSettings stance, StanceSettings into)
        {
            int i = 0;
            while (i < Keys.Length - 2 && t > Keys[i + 1].Time) i++;
            var a = Resolve(Keys[i], stance);
            var b = Resolve(Keys[i + 1], stance);
            float u = Mathf.Clamp01((t - Keys[i].Time) / (Keys[i + 1].Time - Keys[i].Time));
            u = u * u * (3f - 2f * u);
            into.Grip = a.Grip + (b.Grip - a.Grip) * u;
            into.BladeYaw = a.Yaw + (b.Yaw - a.Yaw) * u;
            into.BladePitch = a.Pitch + (b.Pitch - a.Pitch) * u;
            into.EdgeRoll = a.Roll + (b.Roll - a.Roll) * u;
            into.SpineTwist = a.Twist + (b.Twist - a.Twist) * u;
            into.SpineLean = a.Lean + (b.Lean - a.Lean) * u;
            into.LeftOnBlade = a.Left + (b.Left - a.Left) * u;
            into.LeftAlongBlade = stance.LeftAlongBlade;
            // off the blade the left hand keeps hold of the scabbard's mouth, it does not let go of it in the draw
            into.LeftHand = Moves.Mouth(stance);
            into.MouthAxis = Moves.MouthAxis(stance);
            into.HeadForward = stance.HeadForward;
            into.RightElbow = stance.RightElbow;
            into.LeftElbow = stance.LeftElbow;
            into.Attach = null;
        }

        private static Key Resolve(Key k, StanceSettings stance) => !k.Stance ? k : new Key
        {
            Time = k.Time, Grip = stance.Grip, Yaw = stance.BladeYaw, Pitch = stance.BladePitch, Roll = stance.EdgeRoll,
            Twist = stance.SpineTwist, Lean = stance.SpineLean, Left = 1f
        };
    }

    // the flametal katana's thunderclap: down into a low crouch with the blade sheathed while it gathers, then the draw
    // and cut while the player flashes forward, held low past the enemy, the blade back into its scabbard, and up into
    // the stance again. times from the start, prep is how long it gathers
    internal static class Thunder
    {
        internal const float CrouchIn = 0.3f; // seconds to sink into the crouch
        internal const float Cut = 0.12f;     // the draw and cut, as long as the dash
        internal const float Hold = 0.8f;     // the cut held low past the enemy
        internal const float Sheathe = 0.45f; // the blade back into its scabbard
        internal const float Rise = 0.35f;    // up out of the crouch into the stance

        internal static float SheathedAt(float prep) => prep + Cut + Hold + Sheathe;
        internal static float Length(float prep) => SheathedAt(prep) + Rise;

        private struct Key
        {
            internal float Time, Yaw, Pitch, Roll, Twist, Lean, Left;
            internal Vector3 Grip;
        }

        // the crouch's sheathed stance, lower and leaning further than the normal one, the right elbow bent and the
        // wrist straight (11 Oct, StancePreview tune with natural arms on the crouch)
        private static Key Crouched(StanceSettings stance) => new Key
        {
            Grip = stance.Grip + new Vector3(-0.1f, -0.17f, 0.26f), Yaw = stance.BladeYaw - 15f, Pitch = stance.BladePitch - 11f,
            Roll = stance.EdgeRoll - 5f, Twist = stance.SpineTwist + 3f, Lean = stance.SpineLean + 35f, Left = 1f
        };

        // the cut, after the prep: draw, across the front, out to the right and held low there, the blade ahead. the
        // palm stays down all through like the parry's slash, so the forearm never rolls over
        private static readonly Key[] CutKeys =
        {
            new Key { Time = 0.03f, Grip = new Vector3(0.4f, 1.075f, 0.35f), Yaw = 305f, Pitch = -10f, Roll = -128f, Twist = -15f, Lean = 40f, Left = 0f },
            new Key { Time = 0.07f, Grip = new Vector3(0.615f, 0.95f, 0.305f), Yaw = 341f, Pitch = -8f, Roll = -126f, Twist = 10f, Lean = 36f, Left = 0f },
            new Key { Time = Cut, Grip = new Vector3(0.58f, 1.015f, -0.085f), Yaw = 353f, Pitch = 10f, Roll = -130f, Twist = 30f, Lean = 38f, Left = 0f },
        };

        // half way through the sheathing: the blade in front, lifted over the front knee, on its way to the left
        private static readonly Vector3 CrouchLeftElbow = new Vector3(-0.6f, 1f, 0.4f); // the left elbow out over the knee, off the scabbard
        private const float CrouchLeftAlong = 0.4f; // in the crouch the left hand holds the scabbard lower down, where the bent arm reaches it
        private const float SheatheOut = 0.2f; // how far out of the scabbard the blade is laid along it before sliding in (the crouched arm reaches no further)
        private static readonly Key SheatheMid = new Key
        {
            Grip = new Vector3(0.31f, 1.01f, 0.74f), Yaw = 280f, Pitch = -18.5f, Roll = -115f, Twist = -10f, Lean = 35f, Left = 0f
        };

        // the pose t seconds in, written into "into", and how far down the crouch is (0 standing, 1 full crouch)
        internal static void Sample(float t, float prep, StanceSettings stance, StanceSettings into, out float crouch)
        {
            var low = Crouched(stance);
            var up = new Key
            {
                Grip = stance.Grip, Yaw = stance.BladeYaw, Pitch = stance.BladePitch, Roll = stance.EdgeRoll,
                Twist = stance.SpineTwist, Lean = stance.SpineLean, Left = 1f
            };
            Key a, b;
            float u;
            float c = prep + Cut, h = c + Hold, s = h + Sheathe;
            if (t < prep)
            {
                // gathering: sink into the crouch, the blade stays sheathed
                a = up;
                b = low;
                u = t / CrouchIn;
                crouch = Smooth(u);
            }
            else if (t < c)
            {
                float k = t - prep;
                int i = 0;
                while (i < CutKeys.Length - 1 && k > CutKeys[i].Time) i++;
                a = i == 0 ? low : CutKeys[i - 1];
                b = CutKeys[i];
                float from = i == 0 ? 0f : CutKeys[i - 1].Time;
                u = (k - from) / (b.Time - from);
                crouch = 1f;
            }
            else if (t < h)
            {
                a = b = CutKeys[CutKeys.Length - 1];
                u = 0f;
                crouch = 1f;
            }
            else if (t < s)
            {
                // round the front over the knee, laid along the scabbard partly out, then slid in
                float k = (t - h) / Sheathe;
                var lowOut = low;
                var sheathed = new StanceSettings { Grip = low.Grip, BladeYaw = low.Yaw, BladePitch = low.Pitch, EdgeRoll = low.Roll };
                StanceSolver.Sword(Vector3.zero, new Quaternion(0f, 0f, 0f, 1f), sheathed, out _, out _, out var lowBlade);
                lowOut.Grip -= lowBlade * SheatheOut;
                lowOut.Left = 0f;
                if (k < 0.45f)
                {
                    a = CutKeys[CutKeys.Length - 1];
                    b = SheatheMid;
                    u = k / 0.45f;
                }
                else if (k < 0.75f)
                {
                    a = SheatheMid;
                    b = lowOut;
                    u = (k - 0.45f) / 0.3f;
                }
                else
                {
                    a = lowOut;
                    b = low;
                    u = (k - 0.75f) / 0.25f;
                }
                crouch = 1f;
            }
            else
            {
                a = low;
                b = up;
                u = (t - s) / Rise;
                crouch = 1f - Smooth(u);
            }
            u = Smooth(u);
            into.Grip = a.Grip + (b.Grip - a.Grip) * u;
            into.BladeYaw = a.Yaw + (b.Yaw - a.Yaw) * u;
            into.BladePitch = a.Pitch + (b.Pitch - a.Pitch) * u;
            into.EdgeRoll = a.Roll + (b.Roll - a.Roll) * u;
            into.SpineTwist = a.Twist + (b.Twist - a.Twist) * u;
            into.SpineLean = a.Lean + (b.Lean - a.Lean) * u;
            into.LeftOnBlade = a.Left + (b.Left - a.Left) * u;
            // the left hand moves to its crouched hold as the body sinks, and back as it rises
            into.LeftAlongBlade = stance.LeftAlongBlade + (CrouchLeftAlong - stance.LeftAlongBlade) * crouch;
            // off the blade, the left hand holds the scabbard's mouth where the crouch leaves it (saya biki)
            var sheath = new StanceSettings { Grip = low.Grip, BladeYaw = low.Yaw, BladePitch = low.Pitch, EdgeRoll = low.Roll };
            StanceSolver.Sword(Vector3.zero, new Quaternion(0f, 0f, 0f, 1f), sheath, out var sheathGrip, out _, out var sheathBlade);
            into.LeftHand = sheathGrip + sheathBlade * CrouchLeftAlong;
            into.MouthAxis = sheathBlade;
            into.HeadForward = stance.HeadForward;
            into.RightElbow = stance.RightElbow;
            into.LeftElbow = CrouchLeftElbow;
            into.Attach = null;
        }

        private static float Smooth(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * (3f - 2f * u);
        }
    }

    // the katana's own moves, after the iaido forms in the user's clip: a three cut combo laid over the game's greatsword
    // attacks (draw cut, overhead cut, diagonal cut), and the flick and slow sheathing after fighting. a key is a grip
    // point and a blade turn in the player's frame, and the turns are blended as rotations, so a cut can go over the top
    internal static class Moves
    {
        // one moment of a move, everything in the player's frame, measured like the stance from the hips
        internal struct Pose
        {
            internal Vector3 Grip;
            internal Quaternion Rot;   // the attach frame: blade along z, edge along x
            internal float Twist, Lean;
            internal float Left;       // 1 the left hand on the blade or the handle at Along, 0 at the scabbard mouth
            internal float Along;
        }

        private struct Key
        {
            internal float Time;
            internal Vector3 Grip;
            internal float Yaw, Pitch, Roll;  // the blade like the stance's settings
            internal Vector3 Blade, Edge;     // or by direction, for a cut that goes over the top
            internal float Twist, Lean, Left, Along;
            internal bool Sheath;             // the blade lies along the scabbard, Out meters out of it
            internal float Out;
        }

        // the greatsword clips' hit moments (their animation events), which the cuts pass through the middle at
        internal static readonly float[] HitTimes = { 0.604f, 0.492f, 0.639f };
        internal const float NotoBase = 1.5f;    // the sheathing's length its keys are written for
        internal const float NotoClick = 1.45f;  // the guard meets the scabbard here
        internal const float NotoOut = 0.35f;     // how much of the blade is still out when it is laid along the scabbard

        private const float TwoHands = -0.2f;   // the left hand on the handle, behind the right one
        // the blade's curve as a circle (meters, the flametal's is a little tighter than the steel's), from where it
        // starts along the attach frame's z, bending to the back (-x)
        internal const float CurveRadius = 6f;
        private const float CurveFrom = 0.27f;

        // 1: from the scabbard, the draw cut across the front (the parry's slash, with the game's timing). the blade is
        // pulled forward along the scabbard first, then it leaves it into the cut
        private static readonly Key[] DrawCut =
        {
            new Key { Time = 0.3f, Sheath = true, Out = 0.1f },
            new Key { Time = 0.42f, Sheath = true, Out = 0.45f },
            new Key { Time = 0.5f, Grip = new Vector3(0f, 1.26f, 0.52f), Yaw = 320f, Pitch = -5f, Roll = -90f, Twist = -25f, Lean = 10f },
            new Key { Time = 0.604f, Grip = new Vector3(0.12f, 1.28f, 0.46f), Yaw = 380f, Pitch = -3f, Roll = -90f, Twist = -8f, Lean = 12f },
            new Key { Time = 0.72f, Grip = new Vector3(0.34f, 1.25f, 0.28f), Yaw = 445f, Pitch = 5f, Roll = -90f, Twist = 18f, Lean = 10f },
        };

        // 1 with the blade already out: wound up low on the left, the blade back past the hip, then the same cut across
        private static readonly Key[] SideCut =
        {
            new Key { Time = 0.15f, Grip = new Vector3(-0.1f, 1.14f, 0.44f), Yaw = 275f, Pitch = 25f, Roll = -90f, Twist = -30f, Lean = 14f },
            new Key { Time = 0.35f, Grip = new Vector3(-0.15f, 1.15f, 0.45f), Yaw = 240f, Pitch = 0f, Roll = -90f, Twist = -35f, Lean = 8f },
            DrawCut[2], DrawCut[3], DrawCut[4],
        };

        // 2: lifted on the right, the tip back over the right shoulder into the raise above the head (the blade clear of
        // the helmet), the left hand joining on the handle, then straight down to chest height
        private static readonly Key[] OverheadCut =
        {
            new Key { Time = 0.12f, Grip = new Vector3(0.25f, 1.5f, 0.35f), Blade = new Vector3(0.4f, 1f, 0.1f), Edge = new Vector3(0f, 0.1f, -1f), Twist = 10f, Lean = 5f },
            new Key { Time = 0.22f, Grip = new Vector3(0.16f, 1.85f, 0.22f), Blade = new Vector3(0.707f, 0.5f, -0.5f), Edge = new Vector3(-0.707f, 0.5f, -0.5f), Lean = -3f, Left = 0.5f, Along = TwoHands },
            Raised(0.32f),
            new Key { Time = 0.42f, Grip = new Vector3(0.03f, 1.85f, 0.38f), Blade = new Vector3(0f, 1f, 0.25f), Edge = new Vector3(0f, -0.25f, 1f), Lean = 6f, Left = 1f, Along = TwoHands },
            new Key { Time = 0.492f, Grip = new Vector3(0.04f, 1.36f, 0.48f), Blade = new Vector3(0f, -0.1f, 1f), Edge = new Vector3(0f, -1f, -0.1f), Lean = 15f, Left = 1f, Along = TwoHands },
            new Key { Time = 0.6f, Grip = new Vector3(0.04f, 1.24f, 0.45f), Blade = new Vector3(0f, -0.35f, 1f), Edge = new Vector3(0f, -1f, -0.35f), Lean = 18f, Left = 1f, Along = TwoHands },
        };

        // the raise above the head (jodan): both hands high, the blade back over the head, edge up
        private static Key Raised(float time) => new Key
        {
            Time = time, Grip = new Vector3(0.04f, 1.92f, 0.08f), Blade = new Vector3(0f, 0.12f, -1f), Edge = new Vector3(0f, 1f, 0.12f), Lean = -5f, Left = 1f, Along = TwoHands,
        };

        // 3: from over the right shoulder diagonally down across to the left hip (kesa giri), in the plane of the
        // right-up diagonal and forward, the edge leading
        private static readonly Key[] DiagonalCut =
        {
            Diagonal(0.4f, -30f, new Vector3(0.32f, 1.6f, 0.25f), 25f, 0f),
            Diagonal(0.58f, 40f, new Vector3(0.2f, 1.55f, 0.46f), 15f, 8f),
            Diagonal(0.639f, 100f, new Vector3(0.04f, 1.34f, 0.47f), -5f, 14f),
            Diagonal(0.74f, 125f, new Vector3(-0.1f, 1.22f, 0.4f), -10f, 18f),
        };

        private static Key Diagonal(float time, float degrees, Vector3 grip, float twist, float lean)
        {
            var up = QMath.Norm(new Vector3(1f, 1f, 0f));
            var forward = new Vector3(0f, 0f, 1f);
            float a = degrees * Mathf.Deg2Rad;
            return new Key
            {
                Time = time, Grip = grip, Twist = twist, Lean = lean, Left = 1f, Along = TwoHands,
                Blade = up * Mathf.Cos(a) + forward * Mathf.Sin(a),
                Edge = up * -Mathf.Sin(a) + forward * Mathf.Cos(a),
            };
        }

        // after fighting: a flick out to the right (chiburi), the blade brought forward and across the front to the left,
        // laid along the scabbard (noto) and slid in slowly until the guard meets it. it goes round the front of the body,
        // never down past the legs
        // (11 Oct: every key found by StancePreview tune with natural arms, the palm down all through like in the stance
        // and the slash, so the forearm never rolls over)
        private static readonly Key[] Noto =
        {
            new Key { Time = 0.12f, Grip = new Vector3(0.75f, 1.3f, 0f), Yaw = -20f, Pitch = -65f, Roll = -79f, Twist = 15f, Lean = 12f },
            new Key { Time = 0.25f, Grip = new Vector3(0.64f, 1.05f, -0.1f), Yaw = -20f, Pitch = 12f, Roll = -136f, Twist = 0f, Lean = 14f },
            new Key { Time = 0.5f, Grip = new Vector3(0.64f, 1.05f, -0.1f), Yaw = -20f, Pitch = 12f, Roll = -136f, Twist = 0f, Lean = 14f },
            new Key { Time = 0.65f, Grip = new Vector3(0.5f, 1.05f, 0.37f), Yaw = -55f, Pitch = -5f, Roll = -124f, Twist = -25f, Lean = 14f },
            new Key { Time = 0.8f, Grip = new Vector3(0.25f, 1.4f, 0.61f), Yaw = 243f, Pitch = 0f, Roll = -92f, Twist = -40f, Lean = 10f },
            new Key { Time = 0.98f, Sheath = true, Out = NotoOut, Twist = -12f, Lean = 12f },
            new Key { Time = NotoClick, Sheath = true, Lean = 3f },
            new Key { Time = NotoBase, Sheath = true },
        };

        internal static int Steps => 3;

        // the katana's own cuts over the greatsword swings. off for the first beta: they clipped and froze the legs
        // while walking. the game's greatsword and sledge swings play instead, the flick and sheathing after them stay
        internal static readonly bool OwnCuts = false;

        // the part of each cut (clip seconds) its trail is drawn through: from the wind up's end to past the hit
        private static readonly float[] CutFrom = { 0.48f, 0.42f, 0.56f, 1.2f };
        private static readonly float[] CutTo = { 0.74f, 0.62f, 0.76f, 1.38f };

        internal static bool InCut(int step, float clipTime)
        {
            int i = step == HeavyStep ? 3 : step;
            return i >= 0 && i < CutFrom.Length && clipTime >= CutFrom[i] && clipTime < CutTo[i];
        }

        // the heavy attack: nuki uchi, drawn from the scabbard straight up over the head, held there while it gathers, then
        // cut straight down while stepping in with the right foot (fumikomi), ending low with both hands. it follows the
        // sledge's overhead clip the heavy attack borrows (its hit at 1.296 s)
        internal const int HeavyStep = 3;
        internal const float HeavyHit = 1.296f;
        private const float StepFrom = 1.15f, StepIn = 1.3f, StepHold = 1.45f, StepOut = 1.65f;

        private static readonly Key[] DrawUp =
        {
            new Key { Time = 0.1f, Sheath = true, Out = 0.1f },
            new Key { Time = 0.25f, Sheath = true, Out = 0.45f },
            new Key { Time = 0.38f, Grip = new Vector3(0.05f, 1.34f, 0.55f), Yaw = 330f, Pitch = -25f, Roll = -90f, Twist = -20f, Lean = 10f },
            new Key { Time = 0.5f, Grip = new Vector3(-0.02f, 1.78f, 0.25f), Blade = new Vector3(-0.45f, 0.6f, -0.65f), Edge = new Vector3(0.55f, 0.75f, 0.3f), Twist = -5f, Left = 0.5f, Along = TwoHands },
            Raised(0.62f),
            Raised(StepFrom - 0.05f),
            new Key { Time = 1.2f, Grip = new Vector3(0.03f, 1.85f, 0.38f), Blade = new Vector3(0f, 1f, 0.25f), Edge = new Vector3(0f, -0.25f, 1f), Lean = 6f, Left = 1f, Along = TwoHands },
            new Key { Time = HeavyHit, Grip = new Vector3(0.04f, 1.4f, 0.5f), Blade = new Vector3(0f, -0.05f, 1f), Edge = new Vector3(0f, -1f, -0.05f), Lean = 16f, Left = 1f, Along = TwoHands },
            new Key { Time = 1.36f, Grip = new Vector3(0.04f, 1.22f, 0.47f), Blade = new Vector3(0f, -0.3f, 1f), Edge = new Vector3(0f, -1f, -0.3f), Lean = 18f, Left = 1f, Along = TwoHands },
        };

        // the same with the blade already out: lifted on the right and over the right shoulder into the raise, like the
        // overhead cut
        private static readonly Key[] RaiseUp =
        {
            new Key { Time = 0.2f, Grip = new Vector3(0.25f, 1.5f, 0.35f), Blade = new Vector3(0.4f, 1f, 0.1f), Edge = new Vector3(0f, 0.1f, -1f), Twist = 10f, Lean = 5f },
            new Key { Time = 0.4f, Grip = new Vector3(0.16f, 1.85f, 0.22f), Blade = new Vector3(0.707f, 0.5f, -0.5f), Edge = new Vector3(-0.707f, 0.5f, -0.5f), Lean = -3f, Left = 0.5f, Along = TwoHands },
            Raised(0.6f), DrawUp[5], DrawUp[6], DrawUp[7], DrawUp[8],
        };

        // the step into the cut: the right foot forward, the left back with its toes out, the hips down and forward
        internal static readonly CrouchSettings Lunge = new CrouchSettings
        {
            HipDrop = 0.18f, HipBack = -0.15f, HipPitch = 8f, HipTurn = 0f,
            RightFoot = new Vector3(0.12f, 0f, 0.55f), LeftFoot = new Vector3(-0.14f, 0f, -0.35f), RightFootTurn = 0f, LeftFootTurn = -30f,
            RightKnee = new Vector3(0.2f, 0.6f, 1.2f), LeftKnee = new Vector3(-0.25f, 0.2f, 0.3f),
        };

        // the footing under the katana's own cuts: right foot ahead, left foot behind and turned out, knees a little bent
        // (kamae). the whole body is the katana's during them, so the greatsword swing under them can not twist it into
        // the blade
        internal static readonly CrouchSettings Kamae = new CrouchSettings
        {
            HipDrop = 0.04f, HipBack = 0f, HipPitch = 3f, HipTurn = 0f,
            RightFoot = new Vector3(0.13f, 0f, 0.14f), LeftFoot = new Vector3(-0.13f, 0f, -0.22f), RightFootTurn = 5f, LeftFootTurn = -25f,
            RightKnee = new Vector3(0.15f, 0.5f, 1f), LeftKnee = new Vector3(-0.2f, 0.5f, 0.8f),
        };

        // the footing at clipTime of a cut: the kamae, with the heavy attack's step into its cut
        internal static void Footing(int step, float clipTime, CrouchSettings into) =>
            CrouchSettings.Lerp(Kamae, Lunge, step == HeavyStep ? HeavyStepWeight(clipTime) : 0f, into);

        // how far into the step the heavy attack is at clipTime: in with the cut, held, and back before the clip ends
        internal static float HeavyStepWeight(float clipTime)
        {
            float into = Mathf.Clamp01((clipTime - StepFrom) / (StepIn - StepFrom));
            float back = Mathf.Clamp01((clipTime - StepHold) / (StepOut - StepHold));
            into = into * into * (3f - 2f * into);
            back = back * back * (3f - 2f * back);
            return into * (1f - back);
        }

        // one of the three cuts (or the heavy one) at clipTime seconds into its clip, starting from the pose at the clip
        // time it began
        internal static void Combo(int step, bool fromScabbard, float clipTime, float startClip, Pose from, StanceSettings stance, StanceSettings into)
        {
            var keys = step == HeavyStep ? (fromScabbard ? DrawUp : RaiseUp)
                : step == 0 ? (fromScabbard ? DrawCut : SideCut) : step == 1 ? OverheadCut : DiagonalCut;
            Write(Sample(keys, clipTime, Mathf.Min(startClip, keys[0].Time - 0.05f), from, stance), stance, into);
        }

        // the flick and the sheathing, t seconds after it began: held as it was for hold seconds first (the watchful
        // moment after a counter), then the keys stretched to length
        internal static void Sheathe(float t, float hold, float length, Pose from, StanceSettings stance, StanceSettings into)
        {
            float k = t < hold ? 0f : (t - hold) * NotoBase / Mathf.Max(0.1f, length);
            Write(Sample(Noto, k, 0f, from, stance), stance, into);
        }

        // how far out of its scabbard the blade is t seconds into the sheathing, once it is laid along the scabbard and
        // sliding in (the last keys), else -1
        internal static float SlideOut(float t, float hold, float length)
        {
            float k = t < hold ? 0f : (t - hold) * NotoBase / Mathf.Max(0.1f, length);
            int i = 0;
            while (i < Noto.Length && k > Noto[i].Time) i++;
            if (i >= Noto.Length) return Noto[Noto.Length - 1].Out;
            if (i == 0 || !Noto[i - 1].Sheath || !Noto[i].Sheath) return -1f;
            float u = Mathf.Clamp01((k - Noto[i - 1].Time) / (Noto[i].Time - Noto[i - 1].Time));
            return Noto[i - 1].Out + (Noto[i].Out - Noto[i - 1].Out) * (u * u * (3f - 2f * u));
        }

        // the curved blade slid along its own curve by amount meters, out of the scabbard (or back in when negative): a
        // turn about the curve's middle, on the back's side. grip and rot are the attach frame in the player's frame
        internal static void Slide(ref Vector3 grip, ref Quaternion rot, float amount)
        {
            var centre = grip + QMath.Rotate(rot, new Vector3(-CurveRadius, 0f, CurveFrom));
            float half = amount / CurveRadius * 0.5f;
            var axis = QMath.Rotate(rot, new Vector3(0f, 1f, 0f));
            var turn = new Quaternion(axis.x * Mathf.Sin(half), axis.y * Mathf.Sin(half), axis.z * Mathf.Sin(half), Mathf.Cos(half));
            grip = centre + QMath.Rotate(turn, grip - centre);
            rot = QMath.Normalize(QMath.Mul(turn, rot));
        }

        internal static float SheatheLength(float hold, float length) => hold + length;

        // the blade is back in its scabbard (the click), seconds after the sheathing began
        internal static float SheathedAt(float hold, float length) => hold + NotoClick * length / NotoBase;

        // a key as a pose, the scabbard keys taken from the stance
        private static Pose Resolve(Key k, StanceSettings stance)
        {
            if (k.Sheath)
            {
                // the curved blade slides out along its own curve: a turn about the curve's middle, on the back's side.
                // the left hand holds the scabbard mouth all the while
                var sheath = Scabbard(stance);
                if (k.Out != 0f) Slide(ref sheath.Grip, ref sheath.Rot, k.Out);
                sheath.Left = 0f;
                sheath.Twist += k.Twist; // a sheath key's twist and lean add to the stance's, leaning into the sheathing
                sheath.Lean += k.Lean;
                return sheath;
            }
            Quaternion rot;
            if (k.Blade != Vector3.zero) rot = QMath.Look(k.Blade, QMath.Cross(k.Blade, k.Edge));
            else
            {
                var s = new StanceSettings { BladeYaw = k.Yaw, BladePitch = k.Pitch, EdgeRoll = k.Roll };
                StanceSolver.Sword(Vector3.zero, new Quaternion(0f, 0f, 0f, 1f), s, out _, out rot, out _);
            }
            return new Pose { Grip = k.Grip, Rot = rot, Twist = k.Twist, Lean = k.Lean, Left = k.Left, Along = k.Along };
        }

        // the blade in its scabbard: where the scabbard really hangs when that is known, else where the stance aims
        private static Pose Scabbard(StanceSettings stance)
        {
            var sheath = FromSettings(Stance(stance));
            if (!stance.SheathAt.HasValue) return sheath;
            sheath.Grip = stance.SheathGrip;
            sheath.Rot = stance.SheathAt.Value;
            return sheath;
        }

        // the sheathed blade settling into the stance after the sheathing, u from 0 to 1 (the stance may hold it a
        // little apart from where the scabbard hung, and the scabbard follows the blade)
        internal static void Settle(Pose from, float u, StanceSettings stance, StanceSettings into)
        {
            u = Mathf.Clamp01(u);
            Write(Blend(from, FromSettings(stance), u * u * (3f - 2f * u)), stance, into);
        }

        // the stance's own settings without any move's turn
        private static StanceSettings Stance(StanceSettings stance) => new StanceSettings
        {
            Grip = stance.Grip, BladeYaw = stance.BladeYaw, BladePitch = stance.BladePitch, EdgeRoll = stance.EdgeRoll,
            SpineTwist = stance.SpineTwist, SpineLean = stance.SpineLean, LeftAlongBlade = stance.LeftAlongBlade, LeftOnBlade = 1f,
        };

        private static Pose Sample(Key[] keys, float t, float startTime, Pose from, StanceSettings stance)
        {
            Pose a = from, b;
            float ta = startTime, tb;
            int i = 0;
            while (i < keys.Length && t > keys[i].Time) i++;
            if (i >= keys.Length) return Resolve(keys[keys.Length - 1], stance);
            if (i > 0)
            {
                a = Resolve(keys[i - 1], stance);
                ta = keys[i - 1].Time;
            }
            b = Resolve(keys[i], stance);
            tb = keys[i].Time;
            float u = tb > ta ? Mathf.Clamp01((t - ta) / (tb - ta)) : 1f;
            return Blend(a, b, u * u * (3f - 2f * u));
        }

        internal static Pose Blend(Pose a, Pose b, float u) => new Pose
        {
            Grip = a.Grip + (b.Grip - a.Grip) * u,
            Rot = QMath.Nlerp(a.Rot, b.Rot, u),
            Twist = a.Twist + (b.Twist - a.Twist) * u,
            Lean = a.Lean + (b.Lean - a.Lean) * u,
            Left = a.Left + (b.Left - a.Left) * u,
            Along = a.Along + (b.Along - a.Along) * u,
        };

        // the pose a set of stance settings shows (the stance, the slash, the thunderclap or a move)
        internal static Pose FromSettings(StanceSettings s)
        {
            StanceSolver.Sword(Vector3.zero, new Quaternion(0f, 0f, 0f, 1f), s, out _, out var rot, out _);
            return new Pose { Grip = s.Grip, Rot = rot, Twist = s.SpineTwist, Lean = s.SpineLean, Left = s.LeftOnBlade, Along = s.LeftAlongBlade };
        }

        // the middle of the scabbard's mouth, where the left hand holds it, in the player's frame
        internal static Vector3 Mouth(StanceSettings stance)
        {
            var sheath = Scabbard(stance);
            return sheath.Grip + QMath.Rotate(sheath.Rot, new Vector3(0f, 0f, 1f)) * stance.LeftAlongBlade;
        }

        // the scabbard's way from its mouth to the tip, in the player's frame
        internal static Vector3 MouthAxis(StanceSettings stance) => QMath.Rotate(Scabbard(stance).Rot, new Vector3(0f, 0f, 1f));

        private static void Write(Pose p, StanceSettings stance, StanceSettings into)
        {
            into.Grip = p.Grip;
            into.Attach = QMath.Normalize(p.Rot);
            into.SpineTwist = p.Twist;
            into.SpineLean = p.Lean;
            into.LeftOnBlade = p.Left;
            into.LeftAlongBlade = p.Along;
            into.LeftHand = Mouth(stance);
            into.MouthAxis = MouthAxis(stance);
            into.HeadForward = stance.HeadForward;
            into.RightElbow = stance.RightElbow;
            into.LeftElbow = stance.LeftElbow;
        }
    }
}