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

    // smooth timing through a run of keys: every value passes its keys with a speed (a monotone cubic, after Fritsch and
    // Carlson, so it never overshoots a key) and only stops where it turns back, where two keys hold still, or at the
    // run's ends. the old ease on every segment stopped each move at every key, which made the sheathing jiggle
    internal static class Spline
    {
        // the value at x in the segment from (t1, v1) to (t2, v2), with the neighbours (t0, v0) and (t3, v3). a NaN
        // neighbour time ends the run there and the value comes to rest
        internal static float Through(float t0, float t1, float t2, float t3, float v0, float v1, float v2, float v3, float x) =>
            Hermite(t1, t2, v1, v2, float.IsNaN(t0) ? 0f : Slope(t0, t1, t2, v0, v1, v2), float.IsNaN(t3) ? 0f : Slope(t1, t2, t3, v1, v2, v3), x);

        // the speed at the middle point: 0 where the value turns back or holds, else a weighted harmonic mean of both
        // sides (never more than three times the slower one, so the curve stays between its keys)
        internal static float Slope(float ta, float tb, float tc, float va, float vb, float vc)
        {
            float h0 = tb - ta, h1 = tc - tb;
            if (h0 <= 1e-5f || h1 <= 1e-5f) return 0f;
            float d0 = (vb - va) / h0, d1 = (vc - vb) / h1;
            if (d0 * d1 <= 0f) return 0f;
            float w0 = 2f * h1 + h0, w1 = h1 + 2f * h0;
            return (w0 + w1) / (w0 / d0 + w1 / d1);
        }

        // the cubic from (t1, v1) to (t2, v2), leaving with the speed m1 and arriving with m2
        internal static float Hermite(float t1, float t2, float v1, float v2, float m1, float m2, float x)
        {
            float h = t2 - t1;
            if (h <= 1e-5f) return v2;
            float s = Mathf.Clamp01((x - t1) / h), s2 = s * s, s3 = s2 * s;
            return (2f * s3 - 3f * s2 + 1f) * v1 + (s3 - 2f * s2 + s) * h * m1 + (3f * s2 - 2f * s3) * v2 + (s3 - s2) * h * m2;
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
        internal float HeadDown;            // and this many degrees below level (the thunderclap's finish looks at the ground ahead)
        // the right hand on the grip in front of the belly, the elbow bent and hanging down the side, the forearm across
        // the belly and the wrist straight (found by StancePreview tune, the user's drawing of 11 Oct)
        internal Vector3 Grip = new Vector3(-0.05f, 1.12f, 0.38f);
        internal float BladeYaw = 222f;     // the blade points back and out past the left hip (180 is straight back, which runs through a cape), the scabbard clear of the forward left thigh while walking
        internal float BladePitch = 26f;    // and slopes down
        internal float EdgeRoll = -104f;    // the edge turned out from up, so the right hand lies on the grip palm down with a straight wrist
        internal float LeftAlongBlade = 0.3f; // the left hand on the scabbard this far from the grip, the arm hanging relaxed
        internal float LeftOnBlade = 1f;      // 1 the left hand is on the blade, 0 it is at LeftHand (pulled back to the hip in the slash)
        internal Vector3 LeftHand = new Vector3(-0.24f, 1.0f, -0.04f);
        internal Vector3 RightElbow = new Vector3(0.37f, 1.17f, 0.2f); // elbows bend toward these points
        internal Vector3 LeftElbow = new Vector3(-0.8f, 1.19f, -0.12f);
        internal float RightNatural = 0.8f; // 1: the elbows point wherever the arm is most natural, less: pulled toward these poles
        internal float LeftNatural = 0.8f;
        internal float RightWristLimit = 40f; // how far the right wrist bends before the arm solver turns the elbow instead (a move may allow more)
        internal Quaternion? Attach;          // set by the moves: the blade's turn in the player's frame, in place of yaw, pitch and roll
        internal Quaternion? SheathAt;        // where the scabbard really hangs (the blade's turn and SheathGrip), when the game knows it.
        internal Vector3 SheathGrip;          // the sheathing slides the blade into it, not into the stance's own aim
        internal Vector3 MouthAxis;           // the scabbard's way from the mouth to the tip, where the left hand holds it off the blade (zero: the blade's)
        internal float HipTurn;               // the hips' turn in the thunderclap's crouch, degrees (CrouchSettings.HipTurn): its cut turns them into the swing
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

        // how far a body bone (the hips, the chest) is turned about the body's upright against the facing, degrees,
        // positive to the right (the rig's hips and spine face straight ahead at rest)
        internal static float Yaw(Quaternion rootRot, Quaternion bone)
        {
            var f = QMath.Rotate(QMath.Mul(QMath.Conj(rootRot), bone), new Vector3(0f, 0f, 1f));
            return (float)Math.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        // how far the chest turns back to face ahead (it is turned chestYaw from the facing), share hold of it. the
        // stance's own turn then sits on a chest square to the facing, the way the arms were made, whatever the game's
        // idle, walk, run or block does to the chest
        internal static float ChestBack(float chestYaw, float hold) => Wrap(chestYaw) * Mathf.Clamp01(hold);

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

        // how strained a wrist is: a little inside the comfortable ranges, steeply more outside them. a two-handed sword grip
        // bends the wrists far toward the little finger (kendo's wringing grip), so that side goes to 38 degrees
        internal static float Strain(float flex, float thumb, float roll, float flexLimit = 40f)
        {
            float Over(float v, float low, float high) => Math.Max(0f, Math.Max(low - v, v - high));
            float Sq(float x) => x * x;
            return Sq(flex / 45f) + Sq(thumb / 25f) + 0.5f * Sq(roll / 90f)
                   + 4f * (Sq(Over(flex, -flexLimit, flexLimit) / 10f) + Sq(Over(thumb, -38f, 15f) / 10f) + Sq(Over(roll, -90f, 90f) / 10f));
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
                Chest = QMath.Euler(s.SpineLean, s.SpineTwist + s.HipTurn, 0f);
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

        // a direction turned from a toward b by share u of the angle between them, over the up side when they are opposite
        private static Vector3 Toward(Vector3 a, Vector3 b, float u, Vector3 up)
        {
            a = QMath.Norm(a);
            b = QMath.Norm(b);
            var axis = QMath.Cross(a, b);
            if (QMath.Length(axis) < 1e-3f) axis = QMath.Cross(a, up);
            if (QMath.Length(axis) < 1e-3f) axis = QMath.Cross(a, new Vector3(1f, 0f, 0f));
            float angle = (float)Math.Acos(Mathf.Clamp(QMath.Dot(a, b), -1f, 1f)) * Mathf.Rad2Deg * u;
            return QMath.Norm(QMath.Rotate(AxisAngle(QMath.Norm(axis), angle), a));
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
            Vector3 holding = default, Vector3 keepFrom = default, Vector3 keepTo = default, float flexLimit = 40f)
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
                return Strain(flex, thumb, roll, flexLimit) + pull * (1f - QMath.Dot(bend, poleBend)) + 20f * inside;
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

        // the turn the neck takes back so the head looks ahead: the spine's, less HeadDown of its lean
        internal static Quaternion Head(Quaternion rootRot, StanceSettings s) =>
            QMath.Mul(rootRot, QMath.Mul(QMath.Euler(s.SpineLean - s.HeadDown, s.SpineTwist, 0f), QMath.Conj(rootRot)));

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
            var rightBend = Swivel(right, wrist, hand, rightPole, RightRig, s.RightNatural, body, flexLimit: s.RightWristLimit);
            if (memory != null) rightBend = Limit(rightBend, QMath.Norm(wrist - right.Shoulder), rootRot, memory, ref memory.Right);
            Reach(ref right, wrist, right.Shoulder + rightBend, hand, RightRig);

            // the left fist round the scabbard's mouth (or the blade, or the handle behind the right hand): the thumb
            // toward the guard on the scabbard, toward the tip on the handle. the hand turns about the scabbard to meet
            // its forearm, the elbow points where that arm is most natural, a few rounds to settle both
            var leftTarget = LeftTarget(rootPos, rootRot, grip, blade, s);
            var mouth = s.MouthAxis == Vector3.zero ? blade : QMath.Rotate(rootRot, s.MouthAxis);
            float on = Mathf.Clamp01(s.LeftOnBlade);
            // between the scabbard and the handle the thumb turns over the top, never flips
            var thumb = Toward(-mouth, s.LeftAlongBlade < 0f ? blade : -blade, on, QMath.Rotate(rootRot, new Vector3(0f, 1f, 0f)));
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
            // through its keys without stopping at them (Spline): the segment from b to c between a and d
            var b = Resolve(Keys[i], stance);
            var c = Resolve(Keys[i + 1], stance);
            var a = i > 0 ? Resolve(Keys[i - 1], stance) : b;
            var d = i + 2 < Keys.Length ? Resolve(Keys[i + 2], stance) : c;
            float t0 = i > 0 ? a.Time : float.NaN, t3 = i + 2 < Keys.Length ? d.Time : float.NaN;
            float At(float va, float vb, float vc, float vd) => Spline.Through(t0, b.Time, c.Time, t3, va, vb, vc, vd, t);
            into.Grip = new Vector3(At(a.Grip.x, b.Grip.x, c.Grip.x, d.Grip.x), At(a.Grip.y, b.Grip.y, c.Grip.y, d.Grip.y), At(a.Grip.z, b.Grip.z, c.Grip.z, d.Grip.z));
            into.BladeYaw = At(a.Yaw, b.Yaw, c.Yaw, d.Yaw);
            into.BladePitch = At(a.Pitch, b.Pitch, c.Pitch, d.Pitch);
            into.EdgeRoll = At(a.Roll, b.Roll, c.Roll, d.Roll);
            into.SpineTwist = At(a.Twist, b.Twist, c.Twist, d.Twist);
            into.SpineLean = At(a.Lean, b.Lean, c.Lean, d.Lean);
            into.LeftOnBlade = Mathf.Clamp01(At(a.Left, b.Left, c.Left, d.Left));
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
            internal float Time, Yaw, Pitch, Roll, Twist, Lean, Left, Hip, Head;
            internal Vector3 Grip;
        }

        private static readonly float GatherHip = new CrouchSettings().HipTurn; // the crouch's own hip turn while it gathers

        // the crouch's sheathed stance, lower and leaning further than the normal one, the right elbow bent and the
        // wrist straight (11 Oct, StancePreview tune with natural arms on the crouch)
        private static Key Crouched(StanceSettings stance) => new Key
        {
            Grip = stance.Grip + new Vector3(-0.1f, -0.17f, 0.26f), Yaw = stance.BladeYaw - 15f, Pitch = stance.BladePitch - 11f,
            Roll = stance.EdgeRoll - 5f, Twist = stance.SpineTwist + 3f, Lean = stance.SpineLean + 35f, Left = 1f, Hip = GatherHip
        };

        // the cut, after the prep: drawn low from the left hip, a high flat sweep across the front, and held out to the
        // right side with the blade pointing fully to the side. like the dash cuts of anime the body bends deep forward
        // and the head looks low, and the sword arm stays level with the ground (so it rises against the bent chest).
        // the hips and chest turn into it, the way a full horizontal cut is driven: a palm down wrist alone points the
        // blade no further than 55 degrees left of its forearm, so the arm goes out to the side (no more than 30 degrees
        // behind the chest's line) and the body turns. the palm stays down all through like the parry's slash (11 Oct,
        // the user's wish, StancePreview tune)
        private static readonly Key[] CutKeys =
        {
            new Key { Time = 0.03f, Grip = new Vector3(0.45f, 1.06f, 0.32f), Yaw = 310f, Pitch = -10f, Roll = -126f, Twist = -5f, Lean = 41f, Left = 0f, Hip = -15f, Head = 15f },
            new Key { Time = 0.05f, Grip = new Vector3(0.71f, 1.33f, 0.006f), Yaw = 352.7f, Pitch = -4.4f, Roll = -88f, Twist = 10f, Lean = 50.8f, Left = 0f, Hip = 4.9f, Head = 20f },
            new Key { Time = 0.07f, Grip = new Vector3(0.59f, 1.43f, -0.3f), Yaw = 391.5f, Pitch = -5f, Roll = -84f, Twist = 11f, Lean = 53.5f, Left = 0f, Hip = 20f, Head = 25f },
            new Key { Time = Cut, Grip = new Vector3(0.19f, 1.51f, -0.53f), Yaw = 450f, Pitch = -5f, Roll = -75f, Twist = 27f, Lean = 59.5f, Left = 0f, Hip = 35f, Head = 35f },
        };

        // the sheathing starts with the arm still out, the blade swung back to point ahead (the hand goes round the
        // outside, not past the ribs)
        private static readonly Key SheatheBack = new Key
        {
            Grip = new Vector3(0.555f, 1.1f, -0.13f), Yaw = 374.8f, Pitch = -1f, Roll = -118f, Twist = 13.7f, Lean = 55f, Left = 0f, Hip = 5f, Head = 30f
        };

        // half way through the sheathing: the blade in front, lifted over the front knee, on its way to the left
        private static readonly Vector3 CrouchLeftElbow = new Vector3(-0.6f, 1f, 0.4f); // the left elbow out over the knee, off the scabbard
        private const float CrouchLeftAlong = 0.4f; // in the crouch the left hand holds the scabbard lower down, where the bent arm reaches it
        private const float SheatheOut = 0.2f; // how far out of the scabbard the blade is laid along it before sliding in (the crouched arm reaches no further)
        private static readonly Key SheatheMid = new Key
        {
            Grip = new Vector3(0.59f, 1f, 0.52f), Yaw = 315f, Pitch = -15f, Roll = -125f, Twist = 5f, Lean = 39f, Left = 0f, Hip = 3f, Head = 15f
        };

        // the pose t seconds in, written into "into", and how far down the crouch is (0 standing, 1 full crouch)
        internal static void Sample(float t, float prep, StanceSettings stance, StanceSettings into, out float crouch)
        {
            var low = Crouched(stance);
            var up = new Key
            {
                Grip = stance.Grip, Yaw = stance.BladeYaw, Pitch = stance.BladePitch, Roll = stance.EdgeRoll,
                Twist = stance.SpineTwist, Lean = stance.SpineLean, Left = 1f, Hip = GatherHip
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
                // the draw and cut through its keys without stopping at them (Spline)
                var run = new Key[CutKeys.Length + 1];
                var times = new float[CutKeys.Length + 1];
                run[0] = low;
                for (int i = 0; i < CutKeys.Length; i++)
                {
                    run[i + 1] = CutKeys[i];
                    times[i + 1] = CutKeys[i].Time;
                }
                a = b = Through(run, times, t - prep);
                u = 0f;
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
                if (k < 0.75f)
                {
                    // round the outside and over the knee without stopping at the way points (Spline), at rest when
                    // laid along the scabbard
                    a = b = Through(new[] { CutKeys[CutKeys.Length - 1], SheatheBack, SheatheMid, lowOut }, SheatheTimes, k);
                    u = 0f;
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
            into.HipTurn = a.Hip + (b.Hip - a.Hip) * u;
            into.HeadDown = a.Head + (b.Head - a.Head) * u;
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

        // the sheathing's way points, as shares of Sheathe: back round the outside, over the knee, laid on
        private static readonly float[] SheatheTimes = { 0f, 0.2f, 0.45f, 0.75f };

        // the pose at x through the keys of run (at times), each value passing its keys with a speed (Spline), at rest
        // at the run's ends
        private static Key Through(Key[] run, float[] times, float x)
        {
            int i = 0;
            while (i < run.Length - 2 && x > times[i + 1]) i++;
            Key b = run[i], c = run[i + 1];
            Key a = i > 0 ? run[i - 1] : b, d = i + 2 < run.Length ? run[i + 2] : c;
            float t0 = i > 0 ? times[i - 1] : float.NaN, t1 = times[i], t2 = times[i + 1], t3 = i + 2 < run.Length ? times[i + 2] : float.NaN;
            float At(float va, float vb, float vc, float vd) => Spline.Through(t0, t1, t2, t3, va, vb, vc, vd, x);
            return new Key
            {
                Grip = new Vector3(At(a.Grip.x, b.Grip.x, c.Grip.x, d.Grip.x), At(a.Grip.y, b.Grip.y, c.Grip.y, d.Grip.y), At(a.Grip.z, b.Grip.z, c.Grip.z, d.Grip.z)),
                Yaw = At(a.Yaw, b.Yaw, c.Yaw, d.Yaw),
                Pitch = At(a.Pitch, b.Pitch, c.Pitch, d.Pitch),
                Roll = At(a.Roll, b.Roll, c.Roll, d.Roll),
                Twist = At(a.Twist, b.Twist, c.Twist, d.Twist),
                Lean = At(a.Lean, b.Lean, c.Lean, d.Lean),
                Left = Mathf.Clamp01(At(a.Left, b.Left, c.Left, d.Left)),
                Hip = At(a.Hip, b.Hip, c.Hip, d.Hip),
                Head = At(a.Head, b.Head, c.Head, d.Head),
            };
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
            internal float Hip;        // the hips' turn into the cut, degrees (the chest's twist comes on top)
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
            internal float Hip;               // the hips turn into the cut first, the chest's twist comes on top of it
        }

        // the greatsword clips' hit moments (their animation events), which the cuts pass through the middle at. the
        // keys are written in this time
        internal static readonly float[] HitTimes = { 0.604f, 0.492f, 0.639f };

        // the game's upper body swing clips (the snow shovel's, played on the animator's upper body layer so the legs keep
        // walking) and their hit moments: a cut's keys run in the greatsword time above, stretched to these
        internal static readonly string[] UpperClips = { "Attack Shovel 1 Upperbody", "Attack Shovel 2 Upperbody", "Attack Shovel 3" };
        internal static readonly float[] UpperHitTimes = { 0.301f, 0.314f, 0.340f };

        // a cut's key time for clipTime seconds into its upper body clip (step HeavyStep: the heavy on the last clip)
        internal static float KeyTime(int step, float clipTime)
        {
            float key = step == HeavyStep ? HeavyHit : HitTimes[step];
            float clip = UpperHitTimes[step == HeavyStep ? 2 : step];
            return clipTime * key / clip;
        }
        internal const float NotoBase = 1.5f;    // the sheathing's length its keys are written for
        internal const float NotoClick = 1.45f;  // the guard meets the scabbard here
        internal const float NotoOut = 0.35f;     // how much of the blade is still out when it is laid along the scabbard

        private const float TwoHands = -0.2f;   // the left hand on the handle, behind the right one
        // the blade's curve as a circle (meters, the flametal's is a little tighter than the steel's), from where it
        // starts along the attach frame's z, bending to the back (-x)
        internal const float CurveRadius = 6f;
        private const float CurveFrom = 0.27f;

        // 1: from the scabbard, the draw cut across the front (the parry's slash, with the game's timing). the blade is
        // pulled forward along the scabbard first, then it leaves it into the cut. the hips lead it round, the palm stays
        // down. (11 Oct: every key of the cuts found by StancePreview tune with natural arms, on the cut's own footing)
        private static readonly Key[] DrawCut =
        {
            new Key { Time = 0.3f, Sheath = true, Out = 0.1f, Hip = -5f },
            new Key { Time = 0.42f, Sheath = true, Out = 0.45f, Hip = -10f },
            new Key { Time = 0.5f, Grip = new Vector3(0.45f, 1f, 0.388f), Yaw = -48.6f, Pitch = -12f, Roll = -129.5f, Twist = -30f, Lean = 14f, Hip = -12f },
            new Key { Time = 0.604f, Grip = new Vector3(0.593f, 1.053f, 0f), Yaw = -25f, Pitch = -6f, Roll = -132.2f, Twist = 10f, Lean = 15f, Hip = 17.2f },
            new Key { Time = 0.72f, Grip = new Vector3(0.556f, 1.059f, -0.25f), Yaw = -7.2f, Pitch = 0f, Roll = -132.2f, Twist = 15f, Lean = 13f, Hip = 17f },
        };

        // 1 with the blade already out: wound up low on the left, the blade back past the hip and its tip a little up (over
        // the left forearm), then the same cut across
        private static readonly Key[] SideCut =
        {
            new Key { Time = 0.15f, Grip = new Vector3(0.119f, 1.37f, 0.6f), Yaw = 225f, Pitch = -15f, Roll = -77.016f, Twist = -11.375f, Lean = 15f, Hip = -29f },
            new Key { Time = 0.35f, Grip = new Vector3(0.287f, 1f, 0.495f), Yaw = 270.8f, Pitch = -5f, Roll = -136.1f, Twist = -35f, Lean = 8f, Hip = -14.6f },
            DrawCut[2], DrawCut[3], DrawCut[4],
        };

        // 2: swung round in front on the right with the arm long (a straight line in would pull the hand in past the
        // shoulder), lifted, the tip back over the right shoulder into the raise above the head, both hands on the
        // handle, then straight down to chest height. two hands wring the grip (the wrists bend toward the little finger)
        private static readonly Key[] OverheadCut =
        {
            new Key { Time = 0.06f, Grip = new Vector3(0.535f, 1.1f, 0.15f), Yaw = -10f, Pitch = -50f, Roll = -130.536f, Twist = 15f, Lean = 14f, Hip = 15f },
            new Key { Time = 0.12f, Grip = new Vector3(0.1f, 1.35f, 0.507f), Yaw = 45f, Pitch = -75f, Roll = -229f, Twist = 20f, Lean = 5.25f, Hip = 5.25f, Left = 1f, Along = TwoHands },
            new Key { Time = 0.22f, Grip = new Vector3(0.159f, 1.785f, 0.263f), Yaw = 171.215f, Pitch = -15.771f, Roll = 9.646f, Twist = 14.625f, Lean = -6.501f, Hip = 9.191f, Left = 1f, Along = TwoHands },
            Raised(0.32f),
            new Key { Time = 0.42f, Grip = new Vector3(0.055f, 1.62f, 0.568f), Yaw = -14.747f, Pitch = -105f, Roll = 2.403f, Twist = 6.821f, Lean = 11.308f, Hip = 10f, Left = 1f, Along = TwoHands },
            new Key { Time = 0.492f, Grip = new Vector3(-0.15f, 1.2f, 0.393f), Yaw = 8f, Pitch = -12f, Roll = -240f, Twist = -6.1f, Lean = 18.2f, Hip = -10f, Left = 1f, Along = TwoHands },
            new Key { Time = 0.6f, Grip = new Vector3(-0.12f, 1.15f, 0.411f), Yaw = 8f, Pitch = -12f, Roll = -239f, Twist = -6.229f, Lean = 25f, Hip = -10f, Left = 1f, Along = TwoHands },
        };

        // the raise above the head (jodan): both hands high, the blade back over the head
        private static Key Raised(float time) => new Key { Time = time, Grip = new Vector3(0.028f, 1.95f, 0.273f), Yaw = 160f, Pitch = -15f, Roll = 0.066f, Twist = 10f, Lean = -4.347f, Hip = 2.198f, Left = 1f, Along = TwoHands };

        // 3: from over the right shoulder diagonally down toward the left hip (kesa giri), the blade kept in one
        // diagonal plane with the edge leading (so the hands never twist between keys), the hips turning left with
        // it, stopped at the hit (further down the left wrist would bend past its limit)
        private static readonly Key[] DiagonalCut =
        {
            new Key { Time = 0.4f, Grip = new Vector3(0.209f, 1.509f, 0.45f), Yaw = 129.2f, Pitch = -37.8f, Roll = 63.4f, Twist = 2.684f, Lean = -5f, Hip = 9.096f, Left = 1f, Along = TwoHands },
            new Key { Time = 0.58f, Grip = new Vector3(0f, 1.35f, 0.407f), Yaw = 40.1f, Pitch = -32.8f, Roll = 122.7f, Twist = 15f, Lean = 11f, Hip = -2f, Left = 1f, Along = TwoHands },
            new Key { Time = 0.61f, Grip = new Vector3(-0.15f, 1.25f, 0.394f), Yaw = 14.4f, Pitch = -14f, Roll = 133.2f, Twist = 4.625f, Lean = 8.5f, Hip = -13.448f, Left = 1f, Along = TwoHands },
            new Key { Time = 0.639f, Grip = new Vector3(-0.223f, 1.05f, 0.3f), Yaw = 0f, Pitch = 0f, Roll = 120.199f, Twist = -9.793f, Lean = 22f, Hip = -38f, Left = 1f, Along = TwoHands },
            new Key { Time = 0.74f, Grip = new Vector3(-0.25f, 1.12f, 0.4f), Yaw = 5f, Pitch = -10f, Roll = 120.881f, Twist = 0f, Lean = 20f, Hip = -34.25f, Left = 1f, Along = TwoHands },
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

        // the same after a cut that ends low with both hands (the overhead, the heavies): first both hands lift in front,
        // the blade turning up and the right hand rolling halfway, so the pommel never swings down through the right
        // thigh (forward and high in the lunge the body is still rising out of) and the arm is not stretched (tuned
        // 12 Oct, cost 0.22)
        private static readonly Key[] NotoFromLow = WithFirst(Noto,
            new Key { Time = 0.05f, Grip = new Vector3(0.097f, 1.32f, 0.523f), Yaw = 55.4f, Pitch = -60f, Roll = 108.3f, Left = 1f, Along = TwoHands, Twist = 8f, Lean = 18f });

        // the same after the heavies (13 Oct), which end with the edge straight down and the right hand rolled the draw's
        // way: both hands lift the blade up in front of the face, the hand keeping its roll (tuned on the heavy's end
        // body, cost 0.22). the overhead's lift would roll the hand over, and no lift swept the handle through the thighs
        private static readonly Key[] NotoFromHeavy = WithFirst(Noto,
            new Key { Time = 0.05f, Grip = new Vector3(0.12f, 1.3f, 0.51f), Yaw = 45f, Pitch = -73.5f, Roll = 184.9f, Left = 1f, Along = TwoHands, Twist = 8f, Lean = 18f });

        private const float NotoLowGrip = 1.3f; // a start below this with both hands on the handle lifts first

        // the edge (the attach frame's +x) points nearly straight down: the heavies' finish, where the right hand is
        // rolled the draw's way. the overhead's finish turns the edge about 50 degrees to the side
        private static bool EdgeDown(Pose pose) => QMath.Rotate(pose.Rot, new Vector3(1f, 0f, 0f)).y < -0.9f;

        private static Key[] WithFirst(Key[] keys, Key first)
        {
            var all = new Key[keys.Length + 1];
            all[0] = first;
            System.Array.Copy(keys, 0, all, 1, keys.Length);
            return all;
        }

        internal static int Steps => 3;

        // the katana's own cuts over the game's swings. off in 0.1.x (they clipped and froze the legs while walking,
        // the greatsword and sledge swings played instead). on again with the upper body swings (Swing.UpperBody), which
        // leave the legs to the game's walk, set at start
        internal static bool OwnCuts;

        // the part of each cut (clip seconds) its trail is drawn through: from the wind up's end to past the hit
        private static readonly float[] CutFrom = { 0.48f, 0.42f, 0.56f, 1.2f };
        private static readonly float[] CutTo = { 0.74f, 0.62f, 0.76f, 1.38f };

        // a full-body cut's trail, per cut (the combo's three, the heavy): from its top through the strike and the
        // follow-through, clip seconds
        private static readonly float[] FullCutFrom = { 0.29f, 0.236f, 0.3f, 0.62f, 0.62f };
        private static readonly float[] FullCutTo = { 0.75f, 0.62f, 0.75f, 1.45f, 1.45f };
        private static int Slot(int step) => step == HeavyStep ? 3 : step;

        // the steel heavy with the blade already out (Swing.CustomAnimations): its own clip on the game's greatsword_secondary
        // state ("Greatsword Secondary Attack", the hit at 0.959), so the hand never snaps back to the scabbard first
        internal const int HeavyOutStep = 4;
        internal const float HeavyOutHit = 1.296f; // on the sheathed heavy's clock (its clip carries the sledge's events)

        // the clip time of a cut's hit, and where its strike begins (the warp's fast second half): its whoosh plays there
        internal static float HitOf(int step) => step == HeavyStep ? HeavyHit : step == HeavyOutStep ? HeavyOutHit : HitTimes[Mathf.Clamp(step, 0, HitTimes.Length - 1)];
        internal static float FullStrikeAt(int step) => HitOf(step) * 0.5f;

        // a full-body cut's trail window, also for the katana's own clips of it (Swing.CustomAnimations)
        internal static bool InFullCut(int step, float clipTime)
        {
            int i = Slot(step);
            return i >= 0 && i < FullCutFrom.Length && clipTime >= FullCutFrom[i] && clipTime < FullCutTo[i];
        }

        internal static bool InCut(int step, float clipTime)
        {
            if (HasFullBody(step)) return InFullCut(step, clipTime);
            int i = step == HeavyStep ? 3 : step;
            return i >= 0 && i < CutFrom.Length && clipTime >= CutFrom[i] && clipTime < CutTo[i];
        }

        // the heavy attack: nuki uchi, drawn from the scabbard straight up over the head, held there while it gathers, then
        // cut straight down while stepping in with the right foot (fumikomi), ending low with both hands. it follows the
        // sledge's overhead clip the heavy attack borrows (its hit at 1.296 s)
        internal const int HeavyStep = 3;
        internal const float HeavyHit = 1.296f;
        // the right elbow pulled out from the body over these clip seconds, in a slow part: the sheathed heavy while the
        // left hand joins the handle on the way up (13 Oct, the user's test of candidate 4: rising into jodan the elbow
        // still sat in front of the chest and flicked out at the charge), the diagonal at the start of its windup. the
        // heavy with the blade out starts in jodan with the elbow out
        private const float HeavyOutFrom = 0.3f, HeavyOutTo = 0.38f, DiagonalOutFrom = 0f, DiagonalOutTo = 0.12f;
        private static readonly Vector3 ElbowOutPole = new Vector3(0.6f, 0.85f, 0.25f); // out to the right and low, from the hips
        private const float ElbowOutNatural = 0.3f, ElbowOutWristLimit = 80f;

        // in the draw's follow-through two ways of the right arm cost about the same (the hand's roll held at 90, or about
        // 65 with the elbow 3 cm further in). a pole out to the right, pulled fully, holds the first over these clip
        // seconds, in and out smoothly, so the solver does not swap between them (a weaker pull still swaps)
        private const float DrawOutIn = 0.62f, DrawOutFrom = 0.68f, DrawOutTo = 0.82f, DrawOutOut = 0.9f;
        private static readonly Vector3 DrawPole = new Vector3(0.6f, 1.1f, 0.05f);
        private const float DrawNatural = 0f;

        private static float DrawElbowWeight(int step, float clipTime) =>
            step != 0 ? 0f : Smooth((clipTime - DrawOutIn) / (DrawOutFrom - DrawOutIn)) * (1f - Smooth((clipTime - DrawOutTo) / (DrawOutOut - DrawOutTo)));

        // at the diagonal's strike start the right arm has two ways (the elbow out with the wrist bent about 60, or folded
        // in across the chest with the wrist bent about 30). the elbow out pole is pulled fully over these clip seconds,
        // in and out smoothly, so the solver keeps the elbow out (natural 0.15 still swaps once)
        private const float StrikeHoldIn = 0.26f, StrikeHoldFrom = 0.3f, StrikeHoldTo = 0.39f, StrikeHoldOut = 0.43f;
        private const float StrikeHoldNatural = 0f;

        private static float StrikeHoldWeight(int step, float clipTime) =>
            step != 2 ? 0f : Smooth((clipTime - StrikeHoldIn) / (StrikeHoldFrom - StrikeHoldIn)) * (1f - Smooth((clipTime - StrikeHoldTo) / (StrikeHoldOut - StrikeHoldTo)));

        private static float ElbowOutWeight(int step, float clipTime) =>
            step == HeavyOutStep ? 1f
            : step == HeavyStep ? Mathf.Clamp01((clipTime - HeavyOutFrom) / (HeavyOutTo - HeavyOutFrom))
            : step == 2 ? Mathf.Clamp01((clipTime - DiagonalOutFrom) / (DiagonalOutTo - DiagonalOutFrom)) : 0f;

        private static float Smooth(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * (3f - 2f * u);
        }

        // 0.2.1 (the user's tests: the left elbow jiggled in the diagonal's windup, then, pulled low in front of the chest,
        // it bent in toward the body). in hasso both hands sit at the right shoulder and two ways of the left elbow cost
        // about the same, so the solver swapped between them. the diagonal's baking pulls the left elbow out to the
        // left, like the right elbow's ElbowOutPole on its side, over the whole cut like the right one. a little higher and
        // wider than the right's mirror: (-0.6, 0.85, 0.25) left the left hand flipping 18 degrees a frame in the strike,
        // this one has no jumps and keeps the elbow at least 0.18 m out to its side of the spine
        private static readonly Vector3 DiagonalLeftPole = new Vector3(-0.7f, 1f, 0.3f); // from the feet, the player's frame
        private const float DiagonalLeftNatural = ElbowOutNatural;

        private static float LeftLowWeight(int step, float clipTime) =>
            step != 2 ? 0f : Mathf.Clamp01((clipTime - DiagonalOutFrom) / (DiagonalOutTo - DiagonalOutFrom));
        private const float StepFrom = 1.15f, StepIn = 1.3f, StepHold = 1.45f, StepOut = 1.65f;

        private static readonly Key[] DrawUp =
        {
            new Key { Time = 0.1f, Sheath = true, Out = 0.1f, Hip = -5f },
            new Key { Time = 0.25f, Sheath = true, Out = 0.45f, Hip = -10f },
            new Key { Time = 0.38f, Grip = new Vector3(0.3f, 1.15f, 0.628f), Yaw = -45f, Pitch = -35f, Roll = -115.1f, Twist = -25f, Lean = 6.5f, Hip = -10f },
            new Key { Time = 0.5f, Grip = new Vector3(-0.15f, 1.768f, 0.393f), Yaw = 190f, Pitch = -43.397f, Roll = -35.418f, Twist = 10f, Lean = -3f, Hip = 8f, Left = 0.5f, Along = TwoHands },
            Raised(0.62f),
            Raised(StepFrom - 0.05f),
            new Key { Time = 1.2f, Grip = new Vector3(0.055f, 1.62f, 0.568f), Yaw = -14.747f, Pitch = -105f, Roll = 2.403f, Twist = 6.821f, Lean = 11.308f, Hip = 10f, Left = 1f, Along = TwoHands },
            new Key { Time = HeavyHit, Grip = new Vector3(-0.15f, 1.2f, 0.4f), Yaw = 2.7f, Pitch = -16f, Roll = 115.413f, Twist = 8.939f, Lean = 22f, Hip = -8.556f, Left = 1f, Along = TwoHands },
            new Key { Time = 1.36f, Grip = new Vector3(-0.12f, 1.15f, 0.411f), Yaw = 8f, Pitch = -12f, Roll = -239f, Twist = -6.229f, Lean = 25f, Hip = -10f, Left = 1f, Along = TwoHands },
        };

        // the same with the blade already out: lifted on the right and over the right shoulder into the raise, like the
        // overhead cut
        private static readonly Key[] RaiseUp =
        {
            new Key { Time = 0.2f, Grip = new Vector3(0.1f, 1.35f, 0.507f), Yaw = 45f, Pitch = -75f, Roll = -229f, Twist = 20f, Lean = 5.25f, Hip = 5.25f, Left = 1f, Along = TwoHands },
            new Key { Time = 0.4f, Grip = new Vector3(0.159f, 1.785f, 0.263f), Yaw = 171.215f, Pitch = -15.771f, Roll = 9.646f, Twist = 14.625f, Lean = -6.501f, Hip = 9.191f, Left = 1f, Along = TwoHands },
            Raised(0.6f), DrawUp[5], DrawUp[6], DrawUp[7], DrawUp[8],
        };

        // the same from a cut that ended with the arm out on the right (the draw cut): swung round in front first, the
        // arm long, like the overhead cut
        private static readonly Key[] RaiseUpWide =
        {
            new Key { Time = 0.1f, Grip = new Vector3(0.535f, 1.1f, 0.15f), Yaw = -10f, Pitch = -50f, Roll = -130.536f, Twist = 15f, Lean = 14f, Hip = 15f },
            RaiseUp[0], RaiseUp[1], RaiseUp[2], RaiseUp[3], RaiseUp[4], RaiseUp[5], RaiseUp[6],
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

        // the footing at clipTime of a cut: the kamae, with the heavy attack's step into its cut. a full-body cut has its
        // own feet and hips on its body keys
        internal static void Footing(int step, float clipTime, CrouchSettings into)
        {
            if (HasFullBody(step))
            {
                SampleBody(FullBodyOf(step), clipTime, BodyNow);
                WriteFooting(BodyNow, into);
                return;
            }
            CrouchSettings.Lerp(Kamae, Lunge, step == HeavyStep ? HeavyStepWeight(clipTime) : 0f, into);
        }

        // the full-body cuts (Swing.CustomAnimations, 12 Oct): the cut owns the whole body on the base layer, over the game's
        // greatsword swing, so that swing's trail and whoosh events, its hit stop and its network sync all stay. the
        // arms and the blade run on their keys, the hips, chest, head and feet on body keys of their own, so the hips can
        // lead the chest and the chest the arms (the kinetic chain). keyed for range and weight, not for comfort
        internal static bool FullBody;

        // whether the katana's own pose plays this combo step: every step with the upper body swings, with the full-body
        // cuts only the steps that have full-body keys (the others stay the game's greatsword swings)
        internal static bool Owns(int step) => OwnCuts && (!FullBody || HasFullBody(step));

        internal static bool HasFullBody(int step) => FullBody && step >= 0 && step <= HeavyOutStep; // the combo, the heavy, the heavy with the blade out

        private struct BodyKey
        {
            internal float Time;
            internal float Hip, Twist, Lean, Head; // the hips' turn, the chest's twist and lean on top of it, the head this far below level
            internal float Drop, Back, Pitch;      // the hips down, back (negative: forward over the front foot), the pelvis tipped forward
            internal Vector3 Right, Left;          // the feet in the player's frame (y lifts a foot off the ground)
            internal float RightTurn, LeftTurn;
        }

        // 2, full body: kiri oroshi. the blade swings up wide on the right into a high jodan, drops further back for a
        // moment (cocked), then comes straight down the middle with the edge leading, through the hit at chest height and
        // on past it until the tip is low in front, held there (zanshin). the right foot lifts as the blade reaches the
        // top and stamps forward with the cut (fumikomi), the back foot is dragged up after (okuri ashi). the hips start
        // forward and down while the arms are still cocked, the chest follows late and bends deep past the hit (the
        // anime finish), the head goes down last. keyed in the greatsword clip's time: the warp plays 0 to 0.246 as the
        // slow windup (about 0.41 s) and 0.246 to the hit at 0.492 as the strike (about 0.08 s, FullBodyStrikeBoost 1).
        // like a kendo strike the hips peak early (about 55% of the way to the hit), the sword reaches its top last
        // (12 Oct night: each key designed as a cut in one plane, then StancePreview tune on this cut's own body moved the
        // grip a little and turned the edge up to 45 degrees off leading, never the blade's direction, so the wrists stay
        // in their limits. on this rig the strike settles left of the middle with the blade 10 degrees right, like the
        // old overhead cut, a straight middle line bends the wrists 55 to 65 degrees)
        private static readonly Key[] FullOverhead =
        {
            // from the draw cut's end the right hand rolls through neutral before the left hand joins (the draw ends palm
            // down, the overhead holds the other way, a direct turn bent the wrist to -71)
            new Key { Time = 0.035f, Grip = new Vector3(0.55f, 1.05f, 0.022f), Yaw = 40f, Pitch = -28.6f, Roll = -152.1f },
            Two(0.07f, new Vector3(0.24f, 1.3f, 0.32f), 54.8f, -45.2f, 107.5f),      // swung up on the right
            Two(0.14f, new Vector3(0.237f, 1.74f, 0.32f), 167.5f, -55f, -13.2f),     // the tip over the right shoulder
            Two(0.19f, new Vector3(-0.01f, 1.995f, 0.142f), 169.3f, -8f, -32.7f),    // jodan, high
            Two(0.235f, new Vector3(-0.02f, 2.019f, 0.025f), 170.5f, 10f, -42f),     // cocked: the tip dropped behind the back
            Two(0.34f, new Vector3(-0.04f, 1.9f, 0.44f), 163.9f, -71.9f, -38.3f),    // coming over the top
            // from here the hands stay near the middle in front, like a real two-handed cut (12 Oct, the user: far left
            // the right arm crossed the belly; tuned again with GripX -0.08 to 0.04)
            // (the grip stays within about 0.52 m out: the left hand sits 0.2 m behind the right, further out its arm locks)
            Two(0.385f, new Vector3(0.04f, 1.51f, 0.52f), 24f, -82f, 116.1f),        // the blade up ahead, the wrists snapping over
            Two(0.43f, new Vector3(-0.08f, 1.36f, 0.43f), 18f, -42f, 85.8f),         // down in front
            Two(0.492f, new Vector3(-0.065f, 1.077f, 0.5f), 18f, -2f, 132.7f),       // the hit, level at the chest, the hands half a meter out
            Two(0.57f, new Vector3(-0.06f, 1.0f, 0.48f), 16f, 14f, 136f),            // on through, the tip low, the arms out in front
            Two(0.66f, new Vector3(-0.08f, 1.04f, 0.44f), 18f, 11f, 119.5f),         // a small recoil up
            Two(0.95f, new Vector3(-0.08f, 1.08f, 0.44f), 18f, -2f, 129.5f),         // risen into zanshin, the tip just below level
        };

        private static Key Two(float time, Vector3 grip, float yaw, float pitch, float roll) =>
            new Key { Time = time, Grip = grip, Yaw = yaw, Pitch = pitch, Roll = roll, Left = 1f, Along = TwoHands };

        private static readonly BodyKey[] FullOverheadBody =
        {
            // (12 Oct: starts where the draw cut ends, its lunge and turn. the right foot pulls back lifted while the blade
            // goes up, then stamps forward again with the cut)
            new BodyKey { Time = 0f, Drop = 0.18f, Back = -0.12f, Pitch = 12f, Hip = 34f, Twist = 22f, Lean = 33f, Head = 15f, Right = new Vector3(0.26f, 0f, 0.58f), Left = new Vector3(-0.15f, 0f, -0.2f), RightTurn = 22f, LeftTurn = -38f },
            new BodyKey { Time = 0.1f, Drop = 0.08f, Back = 0.02f, Pitch = 0f, Hip = 12f, Twist = 6f, Lean = -2f, Head = -5f, Right = new Vector3(0.17f, 0.05f, 0.36f), Left = new Vector3(-0.14f, 0f, -0.22f), RightTurn = 8f, LeftTurn = -28f },
            new BodyKey { Time = 0.16f, Drop = 0.07f, Back = 0f, Pitch = 0f, Hip = 4f, Twist = 4f, Lean = -8f, Head = -4f, Right = new Vector3(0.13f, 0.03f, 0.24f), Left = new Vector3(-0.13f, 0f, -0.22f), RightTurn = 3f, LeftTurn = -26f },
            new BodyKey { Time = 0.22f, Drop = 0.09f, Back = -0.07f, Pitch = 5f, Hip = 0f, Twist = 2f, Lean = -10f, Head = -2f, Right = new Vector3(0.13f, 0.08f, 0.4f), Left = new Vector3(-0.13f, 0f, -0.25f), RightTurn = 2f, LeftTurn = -28f },
            new BodyKey { Time = 0.3f, Drop = 0.12f, Back = -0.11f, Pitch = 9f, Hip = -3f, Twist = 0f, Lean = 4f, Head = 0f, Right = new Vector3(0.13f, 0.05f, 0.52f), Left = new Vector3(-0.13f, 0f, -0.27f), RightTurn = 0f, LeftTurn = -30f },
            new BodyKey { Time = 0.492f, Drop = 0.17f, Back = -0.16f, Pitch = 12f, Hip = -6f, Twist = -2f, Lean = 24f, Head = 5f, Right = new Vector3(0.13f, 0f, 0.6f), Left = new Vector3(-0.14f, 0f, -0.28f), RightTurn = 0f, LeftTurn = -30f },
            new BodyKey { Time = 0.58f, Drop = 0.22f, Back = -0.18f, Pitch = 14f, Hip = -6f, Twist = -2f, Lean = 34f, Head = 14f, Right = new Vector3(0.13f, 0f, 0.6f), Left = new Vector3(-0.14f, 0f, -0.28f), RightTurn = 0f, LeftTurn = -30f },
            new BodyKey { Time = 0.8f, Drop = 0.2f, Back = -0.17f, Pitch = 12f, Hip = -6f, Twist = -2f, Lean = 30f, Head = 12f, Right = new Vector3(0.13f, 0f, 0.6f), Left = new Vector3(-0.14f, 0f, -0.16f), RightTurn = 0f, LeftTurn = -28f },
            new BodyKey { Time = 0.95f, Drop = 0.13f, Back = -0.13f, Pitch = 8f, Hip = -5f, Twist = -2f, Lean = 18f, Head = 6f, Right = new Vector3(0.13f, 0f, 0.6f), Left = new Vector3(-0.14f, 0f, -0.16f), RightTurn = 0f, LeftTurn = -28f },
        };

        private static BodyKey[] FullBodyOf(int step) => step == 0 ? FullDrawBody : step == 2 ? FullDiagonalBody : step == HeavyStep ? FullHeavyBody
            : step == HeavyOutStep ? FullHeavyOutBody : FullOverheadBody;

        private static Key[] FullArmsOf(int step) => step == 0 ? FullDraw : step == 2 ? FullDiagonal : step == HeavyStep ? FullHeavy
            : step == HeavyOutStep ? FullHeavyOut : FullOverhead;

        // the steel heavy, full body: nuki uchi. drawn up out of the scabbard into a high jodan, the blade cocked behind
        // the back and held while the body sinks and the back foot widens (the charge, the heavy's warp plays 0 to 0.648
        // in about 1.2 s), then the hips drive and a huge cut straight down with a long stamping lunge (about 0.1 s), on
        // through the hit at the chest until the tip is just above the ground, the chest bent deep and the head down.
        // keyed in "Sledge-Attack1"'s time (the hit at 1.296, the clip 1.667 s)
        private static readonly Key[] FullHeavy =
        {
            new Key { Time = 0.08f, Sheath = true, Out = 0f },
            new Key { Time = 0.18f, Sheath = true, Out = 0.12f },
            new Key { Time = 0.24f, Sheath = true, Out = 0.35f },
            new Key { Time = 0.3f, Grip = new Vector3(0.37f, 1.213f, 0.441f), Yaw = -55.8f, Pitch = -55.9f, Roll = -88.7f },
            // (13 Oct, the user: the blade turned sideways in the slash. from here on the edge leads the cut, the right
            // hand rolled the draw's way (the old roll turned the edge 70 to 110 degrees off), the cut 4 to 8 degrees
            // right of straight ahead, the hands near the middle, tuned with tune_keys.py hk3 to hk5)
            // (both hands on the handle by 0.38 and the right elbow out on the way up, tune_keys.py eo_h_two2, eo_h_jodan)
            Two(0.38f, new Vector3(-0.1f, 1.745f, 0.308f), 187.2f, -45.8f, -63.8f),
            Two(0.45f, new Vector3(0.076f, 2.061f, 0.041f), 160.2f, -9.3f, -13.1f),    // jodan
            // (13 Oct, the user's tests: the right forearm pressed into the chest through the slash, then with the
            // hands out in front the right elbow folded in across the chest. from the charge on the right elbow points
            // out and down to the right (Moves.ElbowOutWeight), the hands out in front, the elbows bent about 50, the
            // chest a little more upright from the hit on, tuned with tune_keys.py eo_h_*)
            Two(0.6f, new Vector3(0.089f, 2.066f, -0.088f), 156.7f, 7f, 1.9f),         // cocked, charging
            Two(0.645f, new Vector3(0.089f, 2.059f, -0.086f), 157.2f, 9f, 1.9f),
            Two(0.896f, new Vector3(-0.04f, 1.771f, 0.556f), 160.1f, -62.9f, -3.8f),   // over the top
            Two(1.014f, new Vector3(-0.109f, 1.467f, 0.655f), 0f, -83.3f, 162f),       // the blade up ahead
            Two(1.133f, new Vector3(-0.126f, 1.26f, 0.608f), -1f, -61.6f, 176f),
            Two(1.296f, new Vector3(-0.112f, 1.052f, 0.453f), 7.8f, -34.4f, 180f),     // the hit at the chest
            // the hands low out in front, the tip a little up, the elbows bent (a straight arm let the elbow swing round)
            Two(1.4f, new Vector3(-0.098f, 1.005f, 0.441f), 11.6f, -26.5f, 165f),
            Two(1.667f, new Vector3(-0.102f, 1.014f, 0.434f), 10.5f, -27.1f, 160f),
        };

        private static readonly BodyKey[] FullHeavyBody =
        {
            new BodyKey { Time = 0f, Drop = 0.04f, Pitch = 3f, Twist = -33f, Lean = 8f, Right = new Vector3(0.13f, 0f, 0.14f), Left = new Vector3(-0.13f, 0f, -0.22f), RightTurn = 5f, LeftTurn = -25f },
            new BodyKey { Time = 0.2f, Drop = 0.06f, Pitch = 2f, Hip = -8f, Twist = -20f, Lean = 4f, Head = -4f, Right = new Vector3(0.13f, 0f, 0.14f), Left = new Vector3(-0.13f, 0f, -0.24f), RightTurn = 5f, LeftTurn = -26f },
            new BodyKey { Time = 0.45f, Drop = 0.08f, Back = 0.05f, Pitch = 0f, Hip = 6f, Twist = 4f, Lean = -10f, Head = -6f, Right = new Vector3(0.13f, 0f, 0.12f), Left = new Vector3(-0.15f, 0f, -0.3f), RightTurn = 4f, LeftTurn = -30f },
            new BodyKey { Time = 0.62f, Drop = 0.12f, Back = 0.06f, Pitch = 2f, Hip = 4f, Twist = 3f, Lean = -12f, Head = -4f, Right = new Vector3(0.13f, 0.03f, 0.14f), Left = new Vector3(-0.15f, 0f, -0.3f), RightTurn = 3f, LeftTurn = -30f },
            new BodyKey { Time = 0.75f, Drop = 0.13f, Back = -0.04f, Pitch = 6f, Hip = 0f, Twist = 2f, Lean = -10f, Head = -2f, Right = new Vector3(0.14f, 0.09f, 0.4f), Left = new Vector3(-0.15f, 0f, -0.3f), RightTurn = 2f, LeftTurn = -30f },
            new BodyKey { Time = 1f, Drop = 0.2f, Back = -0.18f, Pitch = 12f, Hip = -4f, Twist = 0f, Lean = 10f, Head = 2f, Right = new Vector3(0.15f, 0.05f, 0.75f), Left = new Vector3(-0.15f, 0f, -0.3f), RightTurn = 0f, LeftTurn = -32f },
            // (13 Oct: the chest leans 16 to 22 degrees less from the hit on. bent 46 to 60 degrees with the pelvis, the
            // arms had no room in front of it: the right forearm pressed into the chest, or the elbow swung out to the
            // side with the wrist bent -55)
            new BodyKey { Time = 1.296f, Drop = 0.28f, Back = -0.28f, Pitch = 16f, Hip = -6f, Twist = -2f, Lean = 14f, Head = 14f, Right = new Vector3(0.16f, 0f, 0.85f), Left = new Vector3(-0.15f, 0f, -0.3f), RightTurn = 0f, LeftTurn = -34f },
            new BodyKey { Time = 1.4f, Drop = 0.32f, Back = -0.3f, Pitch = 18f, Hip = -6f, Twist = -2f, Lean = 20f, Head = 28f, Right = new Vector3(0.16f, 0f, 0.85f), Left = new Vector3(-0.15f, 0f, -0.3f), RightTurn = 0f, LeftTurn = -34f },
            new BodyKey { Time = 1.667f, Drop = 0.3f, Back = -0.28f, Pitch = 17f, Hip = -6f, Twist = -2f, Lean = 18f, Head = 25f, Right = new Vector3(0.16f, 0f, 0.85f), Left = new Vector3(-0.15f, 0f, -0.3f), RightTurn = 0f, LeftTurn = -34f },
        };

        // the same heavy with the blade already out: it starts in jodan (the game blends any cut's end into it), sinks
        // and cocks the blade behind the head through the whole charge, and cuts like the sheathed heavy. it runs on the
        // sheathed heavy's clock (its clip carries "Sledge-Attack1"'s events, the hit at 1.296), so the warp gives both
        // heavies the same charge, the same slash (about 95 ms) and the same recovery. our clip is 1.85 s on the
        // greatsword's secondary state, which leaves at the clip's end (about 0.65 s after the hit)
        private static readonly Key[] FullHeavyOut =
        {
            Two(0f, new Vector3(0.076f, 2.061f, 0.041f), 160.2f, -9.3f, -13.1f),      // jodan, the right elbow out
            Two(0.6f, new Vector3(0.089f, 2.066f, -0.088f), 156.7f, 7f, 1.9f),        // cocked, charging
            Two(0.645f, new Vector3(0.089f, 2.059f, -0.086f), 157.2f, 9f, 1.9f),
            Two(0.896f, new Vector3(-0.04f, 1.771f, 0.556f), 160.1f, -62.9f, -3.8f),  // over the top
            Two(1.014f, new Vector3(-0.109f, 1.467f, 0.655f), 0f, -83.3f, 162f),      // the same strike as the sheathed heavy
            Two(1.133f, new Vector3(-0.126f, 1.26f, 0.608f), -1f, -61.6f, 176f),
            Two(1.296f, new Vector3(-0.112f, 1.052f, 0.453f), 7.8f, -34.4f, 180f),    // the hit at the chest
            Two(1.4f, new Vector3(-0.098f, 1.005f, 0.441f), 11.6f, -26.5f, 165f),
            Two(1.85f, new Vector3(-0.102f, 1.014f, 0.434f), 10.5f, -27.1f, 160f),
        };

        private static readonly BodyKey[] FullHeavyOutBody =
        {
            new BodyKey { Time = 0f, Drop = 0.08f, Back = 0.05f, Pitch = 0f, Hip = 6f, Twist = 4f, Lean = -10f, Head = -6f, Right = new Vector3(0.13f, 0f, 0.12f), Left = new Vector3(-0.15f, 0f, -0.3f), RightTurn = 4f, LeftTurn = -30f },
            new BodyKey { Time = 0.62f, Drop = 0.12f, Back = 0.06f, Pitch = 2f, Hip = 4f, Twist = 3f, Lean = -12f, Head = -4f, Right = new Vector3(0.13f, 0.03f, 0.14f), Left = new Vector3(-0.15f, 0f, -0.3f), RightTurn = 3f, LeftTurn = -30f },
            new BodyKey { Time = 0.75f, Drop = 0.13f, Back = -0.04f, Pitch = 6f, Hip = 0f, Twist = 2f, Lean = -10f, Head = -2f, Right = new Vector3(0.14f, 0.09f, 0.4f), Left = new Vector3(-0.15f, 0f, -0.3f), RightTurn = 2f, LeftTurn = -30f },
            new BodyKey { Time = 1f, Drop = 0.2f, Back = -0.18f, Pitch = 12f, Hip = -4f, Twist = 0f, Lean = 10f, Head = 2f, Right = new Vector3(0.15f, 0.05f, 0.75f), Left = new Vector3(-0.15f, 0f, -0.3f), RightTurn = 0f, LeftTurn = -32f },
            new BodyKey { Time = 1.296f, Drop = 0.28f, Back = -0.28f, Pitch = 16f, Hip = -6f, Twist = -2f, Lean = 14f, Head = 14f, Right = new Vector3(0.16f, 0f, 0.85f), Left = new Vector3(-0.15f, 0f, -0.3f), RightTurn = 0f, LeftTurn = -34f },
            new BodyKey { Time = 1.4f, Drop = 0.32f, Back = -0.3f, Pitch = 18f, Hip = -6f, Twist = -2f, Lean = 20f, Head = 28f, Right = new Vector3(0.16f, 0f, 0.85f), Left = new Vector3(-0.15f, 0f, -0.3f), RightTurn = 0f, LeftTurn = -34f },
            new BodyKey { Time = 1.85f, Drop = 0.3f, Back = -0.28f, Pitch = 17f, Hip = -6f, Twist = -2f, Lean = 18f, Head = 25f, Right = new Vector3(0.16f, 0f, 0.85f), Left = new Vector3(-0.15f, 0f, -0.3f), RightTurn = 0f, LeftTurn = -34f },
        };

        // 2, full body: kesa giri, a downward diagonal from the overhead cut's finish (13 Oct, the user: the old rising
        // cut led with the back of the blade, the sword sits in the hand one way, so the third cut comes down). the blade
        // rises on the right into hasso, its tip back over the right shoulder, while the hips and chest coil right and
        // the front foot is drawn back (the windup, the warp plays 0 to 0.32 in about 0.53 s), then the hips drive left
        // first and the blade cuts down across the front in one plane with the edge leading, level ahead at the hit, on
        // to low on the left, the right foot stamping, and it settles in a low guard where the sheathing can begin. both
        // hands. keyed in "Greatsword BaseAttack (3)"'s time (the hit at 0.639, the clip 1.5 s)
        private static readonly Key[] FullDiagonal =
        {
            // (designed as one cut plane tilted 28 degrees right of upright, the edge along the way the blade turns, then
            // each key tuned with tune_keys.py. like the heavy the cut stops at its hit with a small drift after it:
            // swept on to the left the edge could not lead, and with the tip low the wrists bend -45 to -55. the hands
            // high and out to the right in hasso: nearer the head the right forearm crossed the top of the chest. the
            // strike starts softly from hasso so its first frames, where the hand can not roll the edge ahead, stay slow.
            // (13 Oct, the user's test of candidate 3: the right elbow folded in across the chest. now the right elbow
            // points out to the right all through (Moves.ElbowOutWeight) in the overhead's hand roll family, so the
            // hand no longer turns over in the windup, the wrist bending up to about 60, tune_keys.py eo_d_*)
            Two(0.12f, new Vector3(0.205f, 1.364f, 0.483f), 81.5f, -64.5f, 103.6f),   // lifted on the right, the blade up
            Two(0.18f, new Vector3(0.336f, 1.539f, 0.432f), 129.1f, -61f, 69.4f),
            Two(0.24f, new Vector3(0.296f, 1.648f, 0.351f), 150.1f, -49.7f, 72.4f),   // hasso, the tip back over the right shoulder
            Two(0.3195f, new Vector3(0.296f, 1.648f, 0.351f), 150.1f, -49.7f, 72.4f), // held in hasso to the strike
            Two(0.38f, new Vector3(0.35f, 1.697f, 0.44f), 144f, -48f, 73f),
            Two(0.43f, new Vector3(0.026f, 1.551f, 0.546f), 86.3f, -51f, 96.5f),      // over the top on the right
            Two(0.53f, new Vector3(-0.12f, 1.155f, 0.512f), 31.5f, -40f, 135.7f),     // down across the front
            Two(0.639f, new Vector3(-0.194f, 1.024f, 0.42f), 18f, -10f, 151.6f),      // the hit, level ahead
            Two(0.75f, new Vector3(-0.25f, 1.042f, 0.42f), 12f, -9f, 167f),           // a little on to the left, the tip level
            Two(0.833f, new Vector3(-0.13f, 1.053f, 0.42f), 4f, -11f, 171.3f),
            Two(1.2f, new Vector3(-0.149f, 1.069f, 0.42f), 12f, -24f, 158.5f),        // settled in a guard, the tip a little up
            Two(1.45f, new Vector3(-0.092f, 1.077f, 0.43f), 14f, -25f, 156.4f),
        };

        private static readonly BodyKey[] FullDiagonalBody =
        {
            // the overhead's finish, then coiled right with the front foot drawn back, the hips driving left first
            // (their peak about 55% of the way to the hit), the right foot stamping with the cut, the chest and head
            // following the blade down to the left
            new BodyKey { Time = 0f, Drop = 0.13f, Back = -0.13f, Pitch = 8f, Hip = -5f, Twist = -2f, Lean = 18f, Head = 6f, Right = new Vector3(0.13f, 0f, 0.6f), Left = new Vector3(-0.14f, 0f, -0.16f), RightTurn = 0f, LeftTurn = -28f },
            new BodyKey { Time = 0.15f, Drop = 0.08f, Back = -0.06f, Pitch = 4f, Hip = 12f, Twist = 14f, Lean = 4f, Head = 2f, Right = new Vector3(0.13f, 0.03f, 0.5f), Left = new Vector3(-0.14f, 0f, -0.2f), RightTurn = 5f, LeftTurn = -30f },
            new BodyKey { Time = 0.3f, Drop = 0.1f, Back = -0.04f, Pitch = 5f, Hip = 24f, Twist = 20f, Lean = 2f, Head = 0f, Right = new Vector3(0.13f, 0.05f, 0.42f), Left = new Vector3(-0.14f, 0f, -0.22f), RightTurn = 8f, LeftTurn = -30f },
            new BodyKey { Time = 0.48f, Drop = 0.15f, Back = -0.12f, Pitch = 9f, Hip = -8f, Twist = 8f, Lean = 12f, Head = 4f, Right = new Vector3(0.13f, 0f, 0.62f), Left = new Vector3(-0.14f, 0f, -0.24f), RightTurn = 0f, LeftTurn = -30f },
            // (the chest leans less from the hit on, like the heavy: bent further the hands sat on the belly)
            new BodyKey { Time = 0.639f, Drop = 0.18f, Back = -0.17f, Pitch = 12f, Hip = -22f, Twist = -10f, Lean = 14f, Head = 16f, Right = new Vector3(0.14f, 0f, 0.66f), Left = new Vector3(-0.15f, 0f, -0.26f), RightTurn = -5f, LeftTurn = -34f },
            new BodyKey { Time = 0.75f, Drop = 0.2f, Back = -0.18f, Pitch = 13f, Hip = -28f, Twist = -18f, Lean = 18f, Head = 22f, Right = new Vector3(0.14f, 0f, 0.66f), Left = new Vector3(-0.15f, 0f, -0.26f), RightTurn = -5f, LeftTurn = -34f },
            new BodyKey { Time = 0.833f, Drop = 0.2f, Back = -0.18f, Pitch = 13f, Hip = -30f, Twist = -20f, Lean = 18f, Head = 22f, Right = new Vector3(0.14f, 0f, 0.66f), Left = new Vector3(-0.15f, 0f, -0.26f), RightTurn = -5f, LeftTurn = -34f },
            new BodyKey { Time = 1.2f, Drop = 0.15f, Back = -0.14f, Pitch = 9f, Hip = -14f, Twist = -8f, Lean = 11f, Head = 12f, Right = new Vector3(0.14f, 0f, 0.66f), Left = new Vector3(-0.15f, 0f, -0.22f), RightTurn = -3f, LeftTurn = -32f },
            new BodyKey { Time = 1.45f, Drop = 0.13f, Back = -0.13f, Pitch = 8f, Hip = -10f, Twist = -5f, Lean = 10f, Head = 9f, Right = new Vector3(0.14f, 0f, 0.66f), Left = new Vector3(-0.15f, 0f, -0.2f), RightTurn = -3f, LeftTurn = -30f },
        };

        // 0, full body: nukitsuke from the stance. the body sinks and coils left while the right hand settles on the grip
        // and the thumb pushes the guard out (a tense windup, the warp plays 0 to 0.302 in about 0.5 s), then the hips
        // unwind first and the blade comes out along the scabbard, leaves it pointing ahead and sweeps across the front
        // to the far right in about 0.1 s, the right foot stamping forward and out. it ends like the thunderclap: the
        // blade fully to the side, the arm about level, the chest bent deep, the head low, the left hand still at the
        // scabbard's mouth (saya biki). keyed in "Greatsword BaseAttack (1)"'s time (the hit at 0.604)
        private static readonly Key[] FullDraw =
        {
            new Key { Time = 0.1f, Sheath = true, Out = 0f },
            new Key { Time = 0.28f, Sheath = true, Out = 0.06f },  // the thumb pushes the guard out (koiguchi)
            new Key { Time = 0.36f, Sheath = true, Out = 0.45f },  // the slide out along the curve
            new Key { Time = 0.42f, Grip = new Vector3(0.45f, 1f, 0.388f), Yaw = -48.6f, Pitch = -12f, Roll = -129.5f },
            // (tuned on the cut's own body with tune_keys.py, palm down: the wrists in their limits, the left arm holds
            // the scabbard back at the hip while the chest turns right, as in saya biki)
            new Key { Time = 0.5f, Grip = new Vector3(0.617f, 1.121f, 0.047f), Yaw = -9.9f, Pitch = 4.6f, Roll = -101.4f },
            new Key { Time = 0.555f, Grip = new Vector3(0.576f, 1.258f, -0.3f), Yaw = 43.3f, Pitch = -10.5f, Roll = -81.8f },
            new Key { Time = 0.604f, Grip = new Vector3(0.697f, 1.32f, -0.33f), Yaw = 76f, Pitch = -4f, Roll = -120.3f },
            new Key { Time = 0.783f, Grip = new Vector3(0.465f, 1.096f, -0.365f), Yaw = 83f, Pitch = -2f, Roll = -119.4f },
            new Key { Time = 1.1f, Grip = new Vector3(0.47f, 1.086f, -0.35f), Yaw = 81f, Pitch = -1f, Roll = -122.1f },
        };

        private static readonly BodyKey[] FullDrawBody =
        {
            new BodyKey { Time = 0f, Drop = 0.04f, Pitch = 3f, Twist = -33f, Lean = 8f, Right = new Vector3(0.13f, 0f, 0.14f), Left = new Vector3(-0.13f, 0f, -0.22f), RightTurn = 5f, LeftTurn = -25f },
            new BodyKey { Time = 0.15f, Drop = 0.1f, Back = 0.02f, Pitch = 6f, Hip = -12f, Twist = -40f, Lean = 16f, Head = 4f, Right = new Vector3(0.14f, 0f, 0.16f), Left = new Vector3(-0.13f, 0f, -0.22f), RightTurn = 5f, LeftTurn = -28f },
            new BodyKey { Time = 0.28f, Drop = 0.14f, Back = 0f, Pitch = 10f, Hip = -18f, Twist = -42f, Lean = 22f, Head = 8f, Right = new Vector3(0.16f, 0.05f, 0.28f), Left = new Vector3(-0.13f, 0f, -0.23f), RightTurn = 8f, LeftTurn = -30f },
            new BodyKey { Time = 0.36f, Drop = 0.16f, Back = -0.06f, Pitch = 12f, Hip = 0f, Twist = -25f, Lean = 25f, Head = 10f, Right = new Vector3(0.2f, 0.04f, 0.45f), Left = new Vector3(-0.14f, 0f, -0.26f), RightTurn = 12f, LeftTurn = -33f },
            new BodyKey { Time = 0.5f, Drop = 0.18f, Back = -0.12f, Pitch = 13f, Hip = 25f, Twist = 0f, Lean = 30f, Head = 14f, Right = new Vector3(0.24f, 0f, 0.55f), Left = new Vector3(-0.15f, 0f, -0.3f), RightTurn = 18f, LeftTurn = -38f },
            new BodyKey { Time = 0.604f, Drop = 0.2f, Back = -0.14f, Pitch = 14f, Hip = 35f, Twist = 22f, Lean = 36f, Head = 18f, Right = new Vector3(0.26f, 0f, 0.58f), Left = new Vector3(-0.16f, 0f, -0.32f), RightTurn = 22f, LeftTurn = -40f },
            new BodyKey { Time = 0.7f, Drop = 0.21f, Back = -0.15f, Pitch = 15f, Hip = 38f, Twist = 26f, Lean = 38f, Head = 20f, Right = new Vector3(0.26f, 0f, 0.58f), Left = new Vector3(-0.16f, 0f, -0.32f), RightTurn = 22f, LeftTurn = -40f },
            new BodyKey { Time = 0.783f, Drop = 0.2f, Back = -0.14f, Pitch = 14f, Hip = 36f, Twist = 24f, Lean = 36f, Head = 18f, Right = new Vector3(0.26f, 0f, 0.58f), Left = new Vector3(-0.16f, 0f, -0.3f), RightTurn = 22f, LeftTurn = -40f },
            new BodyKey { Time = 1.1f, Drop = 0.18f, Back = -0.12f, Pitch = 12f, Hip = 34f, Twist = 22f, Lean = 33f, Head = 15f, Right = new Vector3(0.26f, 0f, 0.58f), Left = new Vector3(-0.15f, 0f, -0.2f), RightTurn = 22f, LeftTurn = -38f },
        };

        // the body of a full-body cut, sampled into these values: hip turn, twist, lean, head, drop, back, pitch, the right
        // foot, the left foot, their turns
        private const int BodyValues = 15;
        private static readonly float[] BodyNow = new float[BodyValues], BodyA = new float[BodyValues], BodyB = new float[BodyValues],
            BodyC = new float[BodyValues], BodyD = new float[BodyValues];

        private static void Values(BodyKey k, float[] v)
        {
            v[0] = k.Hip; v[1] = k.Twist; v[2] = k.Lean; v[3] = k.Head; v[4] = k.Drop; v[5] = k.Back; v[6] = k.Pitch;
            v[7] = k.Right.x; v[8] = k.Right.y; v[9] = k.Right.z; v[10] = k.Left.x; v[11] = k.Left.y; v[12] = k.Left.z;
            v[13] = k.RightTurn; v[14] = k.LeftTurn;
        }

        // the body t seconds into its keys: every value passes its keys with a speed (Spline), at rest at the ends
        private static void SampleBody(BodyKey[] keys, float t, float[] into)
        {
            int i = 0;
            while (i < keys.Length && t > keys[i].Time) i++;
            if (i == 0 || i >= keys.Length)
            {
                Values(keys[i == 0 ? 0 : keys.Length - 1], into);
                return;
            }
            bool before = i >= 2, after = i + 1 < keys.Length;
            Values(keys[i - 1], BodyB);
            Values(keys[i], BodyC);
            Values(keys[before ? i - 2 : i - 1], BodyA);
            Values(keys[after ? i + 1 : i], BodyD);
            float t0 = before ? keys[i - 2].Time : float.NaN, t1 = keys[i - 1].Time, t2 = keys[i].Time, t3 = after ? keys[i + 1].Time : float.NaN;
            for (int v = 0; v < BodyValues; v++) into[v] = Spline.Through(t0, t1, t2, t3, BodyA[v], BodyB[v], BodyC[v], BodyD[v], t);
        }

        private static void WriteFooting(float[] b, CrouchSettings into)
        {
            into.HipTurn = b[0];
            into.HipDrop = b[4];
            into.HipBack = b[5];
            into.HipPitch = b[6];
            into.RightFoot = new Vector3(b[7], b[8], b[9]);
            into.LeftFoot = new Vector3(b[10], b[11], b[12]);
            into.RightFootTurn = b[13];
            into.LeftFootTurn = b[14];
            into.RightKnee = Lunge.RightKnee; // the knees bend forward over the feet
            into.LeftKnee = Lunge.LeftKnee;
        }

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
            if (HasFullBody(step))
            {
                // the arms on their keys, the hips, chest and head on the body keys
                var arms = FullArmsOf(step);
                var pose = Sample(arms, clipTime, Mathf.Min(startClip, arms[0].Time - 0.05f), from, stance);
                SampleBody(FullBodyOf(step), clipTime, BodyNow);
                pose.Hip = BodyNow[0];
                pose.Twist = BodyNow[1];
                pose.Lean = BodyNow[2];
                Write(pose, stance, into);
                into.HeadDown = BodyNow[3];
                // the right elbow points out from the body (13 Oct, the user's test: an elbow folded in across the chest
                // can not be). with both hands low in front and the edge down, the arm solver's most comfortable pick
                // folded the right arm across the chest in the heavies' and the diagonal's strike and finish. there the
                // elbow is pulled out and down to the right, and the wrist may bend up to 80 degrees instead (the
                // user's compromise). only these clips' baking sees it, the game plays the baked clips
                float outWeight = ElbowOutWeight(step, clipTime);
                into.RightNatural = Mathf.Lerp(stance.RightNatural, ElbowOutNatural, outWeight);
                into.RightElbow = stance.RightElbow + (ElbowOutPole - stance.RightElbow) * outWeight;
                into.RightWristLimit = Mathf.Lerp(stance.RightWristLimit, ElbowOutWristLimit, outWeight);
                float drawWeight = DrawElbowWeight(step, clipTime);
                into.RightNatural = Mathf.Lerp(into.RightNatural, DrawNatural, drawWeight);
                into.RightElbow = into.RightElbow + (DrawPole - into.RightElbow) * drawWeight;
                into.RightNatural = Mathf.Lerp(into.RightNatural, StrikeHoldNatural, StrikeHoldWeight(step, clipTime));
                float lowWeight = LeftLowWeight(step, clipTime);
                into.LeftElbow = stance.LeftElbow + (DiagonalLeftPole - stance.LeftElbow) * lowWeight;
                into.LeftNatural = Mathf.Lerp(stance.LeftNatural, DiagonalLeftNatural, lowWeight);
                return;
            }
            var keys = step == HeavyStep ? (fromScabbard ? DrawUp : from.Grip.x > 0.3f ? RaiseUpWide : RaiseUp) // the arm out right: swung round
                : step == 0 ? (fromScabbard ? DrawCut : SideCut) : step == 1 ? OverheadCut : DiagonalCut;
            Write(Sample(keys, clipTime, Mathf.Min(startClip, keys[0].Time - 0.05f), from, stance), stance, into);
        }

        // the flick and the sheathing, t seconds after it began: held as it was for hold seconds first (the watchful
        // moment after a counter), then the keys stretched to length
        internal static void Sheathe(float t, float hold, float length, Pose from, StanceSettings stance, StanceSettings into)
        {
            float k = t < hold ? 0f : (t - hold) * NotoBase / Mathf.Max(0.1f, length);
            // both hands on the handle, low: lift first, the heavies' way when the edge points straight down
            var keys = from.Left > 0.5f && from.Along < 0f && from.Grip.y < NotoLowGrip ? (EdgeDown(from) ? NotoFromHeavy : NotoFromLow) : Noto;
            Write(Sample(keys, k, 0f, from, stance), stance, into);
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
            return OutAt(Noto, i, k, 0f);
        }

        // how far out of its scabbard the blade is at t on the slide from sheath key i - 1 to sheath key i. laid on from a
        // free key it starts at rest, drawn from the start pose (sheathed) it speeds up from there, and leaving the
        // scabbard into a free key it keeps its speed
        private static float OutAt(Key[] keys, int i, float t, float startTime)
        {
            float t1 = keys[i - 1].Time, t2 = keys[i].Time, o1 = keys[i - 1].Out, o2 = keys[i].Out;
            float m1 = i >= 2 ? (keys[i - 2].Sheath ? Spline.Slope(keys[i - 2].Time, t1, t2, keys[i - 2].Out, o1, o2) : 0f)
                : Spline.Slope(startTime, t1, t2, 0f, o1, o2);
            float m2 = i + 1 >= keys.Length ? 0f
                : keys[i + 1].Sheath ? Spline.Slope(t1, t2, keys[i + 1].Time, o1, o2, keys[i + 1].Out)
                : (o2 - o1) / Mathf.Max(1e-5f, t2 - t1);
            return Spline.Hermite(t1, t2, o1, o2, m1, m2, t);
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
                sheath.Twist += k.Twist; // a sheath key's twist, lean and hip turn add to the stance's, leaning into the sheathing
                sheath.Lean += k.Lean;
                sheath.Hip += k.Hip;
                return sheath;
            }
            Quaternion rot;
            if (k.Blade != Vector3.zero) rot = QMath.Look(k.Blade, QMath.Cross(k.Blade, k.Edge));
            else
            {
                var s = new StanceSettings { BladeYaw = k.Yaw, BladePitch = k.Pitch, EdgeRoll = k.Roll };
                StanceSolver.Sword(Vector3.zero, new Quaternion(0f, 0f, 0f, 1f), s, out _, out rot, out _);
            }
            return new Pose { Grip = k.Grip, Rot = rot, Twist = k.Twist, Lean = k.Lean, Left = k.Left, Along = k.Along, Hip = k.Hip };
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

        // the pose t seconds into a move's keys, starting from the pose from at startTime. every value passes its keys with
        // a speed (Spline) and only stops where it turns back, where keys hold still, at the move's ends, and where the
        // blade is laid onto its scabbard. along the scabbard the blade slides exactly on its curve (OutAt), the same
        // slide the scabbard follows (SlideOut)
        private static Pose Sample(Key[] keys, float t, float startTime, Pose from, StanceSettings stance)
        {
            int i = 0;
            while (i < keys.Length && t > keys[i].Time) i++;
            if (i >= keys.Length) return Resolve(keys[keys.Length - 1], stance);
            // the segment from point i - 1 to point i (point -1 is the start pose), between the points i - 2 and i + 1
            bool before = i >= 1, after = i + 1 < keys.Length;
            // laid onto the scabbard from a free key: it arrives at rest, so it slides in straight
            if (i >= 1 && !keys[i - 1].Sheath && keys[i].Sheath) after = false;
            var b = PointAt(keys, i - 1, from, stance);
            var c = Resolve(keys[i], stance);
            var a = before ? PointAt(keys, i - 2, from, stance) : b;
            var d = after ? Resolve(keys[i + 1], stance) : c;
            float t0 = before ? TimeAt(keys, i - 2, startTime) : float.NaN, t1 = TimeAt(keys, i - 1, startTime), t2 = keys[i].Time;
            float t3 = after ? keys[i + 1].Time : float.NaN;
            float At(float va, float vb, float vc, float vd) => Spline.Through(t0, t1, t2, t3, va, vb, vc, vd, t);
            Pose pose;
            if (i >= 1 && keys[i - 1].Sheath && keys[i].Sheath)
                pose = Resolve(new Key { Sheath = true, Out = OutAt(keys, i, t, startTime) }, stance);
            else
                pose = new Pose
                {
                    Grip = new Vector3(At(a.Grip.x, b.Grip.x, c.Grip.x, d.Grip.x), At(a.Grip.y, b.Grip.y, c.Grip.y, d.Grip.y), At(a.Grip.z, b.Grip.z, c.Grip.z, d.Grip.z)),
                    Rot = Turn(a.Rot, b.Rot, c.Rot, d.Rot, t0, t1, t2, t3, t),
                    Left = Mathf.Clamp01(At(a.Left, b.Left, c.Left, d.Left)),
                    Along = At(a.Along, b.Along, c.Along, d.Along),
                };
            pose.Twist = At(a.Twist, b.Twist, c.Twist, d.Twist);
            pose.Lean = At(a.Lean, b.Lean, c.Lean, d.Lean);
            pose.Hip = At(a.Hip, b.Hip, c.Hip, d.Hip);
            return pose;
        }

        private static float TimeAt(Key[] keys, int j, float startTime) => j < 0 ? startTime : keys[j].Time;

        private static Pose PointAt(Key[] keys, int j, Pose from, StanceSettings stance) => j < 0 ? from : Resolve(keys[j], stance);

        // the blade's turn at x from q1 to q2 the short way, timed like the other values: it keeps its speed through a key
        // while the turn goes on the same way round, and stops where it turns back
        private static Quaternion Turn(Quaternion q0, Quaternion q1, Quaternion q2, Quaternion q3, float t0, float t1, float t2, float t3, float x)
        {
            float a12 = Angle(q1, q2);
            if (a12 < 1e-4f) return q2;
            float a01 = 0f, a23 = 0f;
            if (!float.IsNaN(t0))
            {
                if (QMath.Dot(Way(q0, q1), Way(q1, q2)) > 0f) a01 = Angle(q0, q1);
                else t0 = float.NaN;
            }
            if (!float.IsNaN(t3))
            {
                if (QMath.Dot(Way(q1, q2), Way(q2, q3)) > 0f) a23 = Angle(q2, q3);
                else t3 = float.NaN;
            }
            float w = Spline.Through(t0, t1, t2, t3, -a01, 0f, a12, a12 + a23, x) / a12;
            return QMath.Nlerp(q1, q2, Mathf.Clamp01(w));
        }

        // radians between two turns
        private static float Angle(Quaternion a, Quaternion b) => 2f * Mathf.Acos(Mathf.Min(1f, Mathf.Abs(a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w)));

        // the axis (scaled) a turn from a to b goes round, in the player's frame
        private static Vector3 Way(Quaternion a, Quaternion b)
        {
            var r = QMath.Mul(b, QMath.Conj(a));
            var v = new Vector3(r.x, r.y, r.z);
            return r.w < 0f ? -v : v;
        }

        internal static Pose Blend(Pose a, Pose b, float u) => new Pose
        {
            Grip = a.Grip + (b.Grip - a.Grip) * u,
            Rot = QMath.Nlerp(a.Rot, b.Rot, u),
            Twist = a.Twist + (b.Twist - a.Twist) * u,
            Lean = a.Lean + (b.Lean - a.Lean) * u,
            Left = a.Left + (b.Left - a.Left) * u,
            Along = a.Along + (b.Along - a.Along) * u,
            Hip = a.Hip + (b.Hip - a.Hip) * u,
        };

        // the pose a set of stance settings shows (the stance, the slash, the thunderclap or a move)
        internal static Pose FromSettings(StanceSettings s)
        {
            StanceSolver.Sword(Vector3.zero, new Quaternion(0f, 0f, 0f, 1f), s, out _, out var rot, out _);
            return new Pose { Grip = s.Grip, Rot = rot, Twist = s.SpineTwist, Lean = s.SpineLean, Left = s.LeftOnBlade, Along = s.LeftAlongBlade, Hip = s.HipTurn };
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
            into.HipTurn = p.Hip;
            into.LeftHand = Mouth(stance);
            into.MouthAxis = MouthAxis(stance);
            into.HeadForward = stance.HeadForward;
            into.HeadDown = 0f; // a full-body cut sets its own after this
            into.RightElbow = stance.RightElbow;
            into.LeftElbow = stance.LeftElbow;
        }
    }

    // the feet of a full-body cut stay where they stand in the world. the cut's footing moves them (the stamp forward,
    // the back foot dragged up), and that motion is added to where each foot stands. when the player keeps moving through
    // the swing (the game keeps 30% of the speed), a foot that is left too far from where the footing wants it takes a
    // quick lifted step there, one foot at a time. standing still the feet do exactly what the keys say
    internal sealed class FootPlant
    {
        internal const float Drift = 0.1f;     // meters off its footing before a foot steps
        internal const float MaxDrift = 0.2f;  // a standing foot further off than this slides along (the leg would stretch)
        internal const float StepTime = 0.13f; // seconds a step takes
        internal const float Lift = 0.07f;     // how high the stepping foot lifts at its middle
        private const float Lead = 0.6f;       // a step lands this share of its own time ahead of the moving player

        internal bool Has;
        internal int Stepping = -1;           // 0 the left foot steps, 1 the right, -1 none
        internal int Steps;                   // steps taken since the cut began
        private Vector3 _left, _right;        // where each foot stands, world
        private Vector3 _keyLeft, _keyRight;  // the footing last frame, the player's frame
        private Vector3 _from;
        private float _start;
        private Vector3 _lastRoot, _velocity; // how fast the player moves on the ground, smoothed
        private float _lastTime;

        internal void Reset()
        {
            Has = false;
            Stepping = -1;
            Steps = 0;
        }

        // rootPos and rootRot are the player's place now, legs its footing at time (seconds). the feet are written back
        // into legs, in the player's frame
        internal void Step(Vector3 rootPos, Quaternion rootRot, CrouchSettings legs, float time)
        {
            var wantLeft = rootPos + QMath.Rotate(rootRot, Flat(legs.LeftFoot));
            var wantRight = rootPos + QMath.Rotate(rootRot, Flat(legs.RightFoot));
            if (!Has)
            {
                _left = wantLeft;
                _right = wantRight;
                _keyLeft = legs.LeftFoot;
                _keyRight = legs.RightFoot;
                Has = true;
                Stepping = -1;
                Steps = 0;
                _lastRoot = rootPos;
                _lastTime = time;
                _velocity = Vector3.zero;
            }
            float dt = time - _lastTime;
            if (dt > 1e-4f)
            {
                _velocity = Vector3.Lerp(_velocity, Flat(rootPos - _lastRoot) / dt, Mathf.Clamp01(dt / 0.08f));
                _lastRoot = rootPos;
                _lastTime = time;
            }
            // the footing's own motion since last frame moves the standing feet with it
            _left += QMath.Rotate(rootRot, Flat(legs.LeftFoot - _keyLeft));
            _right += QMath.Rotate(rootRot, Flat(legs.RightFoot - _keyRight));
            _keyLeft = legs.LeftFoot;
            _keyRight = legs.RightFoot;
            // a step lands a little ahead when the player moves, so the next one is not due at once
            var lead = _velocity * (StepTime * Lead);
            wantLeft += lead;
            wantRight += lead;
            // a standing foot never falls further behind than MaxDrift: it slides instead of stretching the leg
            if (Stepping != 0) _left = Within(_left, wantLeft, MaxDrift);
            if (Stepping != 1) _right = Within(_right, wantRight, MaxDrift);

            if (Stepping < 0)
            {
                float left = Across(_left - wantLeft), right = Across(_right - wantRight);
                if (Mathf.Max(left, right) > Drift)
                {
                    Stepping = right >= left ? 1 : 0;
                    _from = Stepping == 1 ? _right : _left;
                    _start = time;
                    Steps++;
                }
            }
            // the feet stand on the player's ground height (like the game's own clips, slopes are not followed)
            var shownLeft = _left;
            var shownRight = _right;
            float liftLeft = 0f, liftRight = 0f;
            if (Stepping >= 0)
            {
                // the step lands where the footing wants the foot by then, lifted on the way
                float u = Mathf.Clamp01((time - _start) / StepTime);
                float eased = u * u * (3f - 2f * u);
                var to = Stepping == 1 ? wantRight : wantLeft;
                var at = _from + (to - _from) * eased;
                float lift = Lift * 4f * u * (1f - u);
                if (Stepping == 1)
                {
                    shownRight = at;
                    liftRight = lift;
                }
                else
                {
                    shownLeft = at;
                    liftLeft = lift;
                }
                if (u >= 1f)
                {
                    if (Stepping == 1) _right = to;
                    else _left = to;
                    Stepping = -1;
                }
            }
            legs.LeftFoot = Local(rootPos, rootRot, shownLeft, legs.LeftFoot.y + liftLeft);
            legs.RightFoot = Local(rootPos, rootRot, shownRight, legs.RightFoot.y + liftRight);
        }

        // a foot's place on the ground back in the player's frame, lifted by lift
        private static Vector3 Local(Vector3 rootPos, Quaternion rootRot, Vector3 world, float lift)
        {
            var local = QMath.Rotate(QMath.Conj(rootRot), Flat(world - rootPos));
            return new Vector3(local.x, lift, local.z);
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static float Across(Vector3 v) => Mathf.Sqrt(v.x * v.x + v.z * v.z);

        // p moved toward want until it is at most max away on the ground
        private static Vector3 Within(Vector3 p, Vector3 want, float max)
        {
            float d = Across(p - want);
            return d <= max ? p : want + (p - want) * (max / d);
        }
    }
}