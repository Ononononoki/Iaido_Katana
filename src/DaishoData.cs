using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Iaido_Katana
{
    // the daisho's two moves (the daisho parry and Ranbu), the beta drafts' 180 poses a second for the male and the
    // female skeleton, packed by devtools\session_2026-11-08\daisho_pack.py and embedded in the dll. every client plays
    // the same frames, so the pose, the wakizashi and the effects agree everywhere
    internal sealed class DaishoMove
    {
        // one cut of the move: which blade, when it hits, the window of its strike and the path its tip swept
        internal struct Cut
        {
            internal bool Wakizashi;
            internal float Hit, From, To;
            internal Vector3[] Shape;
        }

        internal sealed class Rig
        {
            internal bool[] Snap;
            internal float[] Bones;  // per frame DaishoData.BoneCount quaternions, x y z w
            internal float[] Hips;   // per frame the hips bone's local position
            internal float[] Waki;   // per frame the wakizashi's place in the player's frame: position, then a rotation x y z w
            internal float[] Saya;   // per frame the wakizashi's scabbard, the same way (it follows the hips)
            internal float[] Kata;   // per frame the katana's grip place in the player's frame: position, then the attach rotation x y z w
            internal float[] Hold;   // per frame how much the left hand holds the wakizashi
            internal float[] Tips;   // per frame both blade tips in the player's frame
        }

        internal string Name;
        internal float Length;
        internal int Frames;
        internal Cut[] Cuts;
        internal readonly Rig[] Rigs = new Rig[2];
    }

    internal static class DaishoData
    {
        internal const int BoneCount = 16;
        internal const float Rate = 180f;
        // the order of the bones in the file
        internal static readonly string[] BoneNames =
        {
            "Hips", "LeftUpLeg", "LeftLeg", "LeftFoot", "RightUpLeg", "RightLeg", "RightFoot", "Spine1", "Spine2", "Neck",
            "RightArm", "RightForeArm", "RightHand", "LeftArm", "LeftForeArm", "LeftHand"
        };
        private const string Resource = "Iaido_Katana.daisho_moves.bin";

        private static DaishoMove _parry, _ranbu;
        private static bool _loaded;

        internal static DaishoMove Parry { get { Load(); return _parry; } }
        internal static DaishoMove Ranbu { get { Load(); return _ranbu; } }

        internal static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource))
                {
                    if (stream == null)
                    {
                        Plugin.Log?.LogWarning("daisho moves: the pose data is not in the dll");
                        return;
                    }
                    Read(stream);
                }
            }
            catch (Exception e)
            {
                _parry = _ranbu = null;
                Plugin.Log?.LogWarning("daisho moves: the pose data could not be read: " + e.Message);
            }
        }

        internal static void Read(Stream stream)
        {
            using (var reader = new BinaryReader(stream))
            {
                if (new string(reader.ReadChars(4)) != "DSH1") throw new InvalidDataException("not a daisho pose file");
                int moves = reader.ReadInt32();
                for (int m = 0; m < moves; m++)
                {
                    var move = new DaishoMove { Name = new string(reader.ReadChars(reader.ReadByte())) };
                    move.Length = reader.ReadSingle();
                    move.Frames = reader.ReadInt32();
                    int cuts = reader.ReadInt32();
                    move.Cuts = new DaishoMove.Cut[cuts];
                    for (int c = 0; c < cuts; c++)
                    {
                        var cut = new DaishoMove.Cut { Wakizashi = reader.ReadByte() == 1, Hit = reader.ReadSingle(), From = reader.ReadSingle(), To = reader.ReadSingle() };
                        cut.Shape = new Vector3[reader.ReadInt32()];
                        for (int i = 0; i < cut.Shape.Length; i++) cut.Shape[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        move.Cuts[c] = cut;
                    }
                    for (int r = 0; r < 2; r++)
                    {
                        int n = move.Frames;
                        var rig = new DaishoMove.Rig
                        {
                            Snap = new bool[n], Bones = new float[n * BoneCount * 4], Hips = new float[n * 3], Waki = new float[n * 7],
                            Kata = new float[n * 7], Saya = new float[n * 7], Hold = new float[n], Tips = new float[n * 6]
                        };
                        for (int f = 0; f < n; f++)
                        {
                            rig.Snap[f] = reader.ReadByte() == 1;
                            for (int i = 0; i < BoneCount * 4; i++) rig.Bones[f * BoneCount * 4 + i] = reader.ReadSingle();
                            for (int i = 0; i < 3; i++) rig.Hips[f * 3 + i] = reader.ReadSingle();
                            for (int i = 0; i < 7; i++) rig.Waki[f * 7 + i] = reader.ReadSingle();
                            for (int i = 0; i < 7; i++) rig.Kata[f * 7 + i] = reader.ReadSingle();
                            for (int i = 0; i < 7; i++) rig.Saya[f * 7 + i] = reader.ReadSingle();
                            rig.Hold[f] = reader.ReadSingle();
                            for (int i = 0; i < 6; i++) rig.Tips[f * 6 + i] = reader.ReadSingle();
                        }
                        move.Rigs[r] = rig;
                    }
                    if (move.Name == "parry") _parry = move;
                    else if (move.Name == "ranbu") _ranbu = move;
                }
            }
        }

        // one frame of a move's pose at time t: the bones' local rotations (BoneNames order), the hips' local position,
        // the wakizashi's place and how much the left hand holds it. between two frames the rotations blend, but never
        // across a snap: the pose holds the frame before it until the snap's time, then jumps
        internal static void Sample(DaishoMove move, int rigIndex, float t, Quaternion[] bones, out Vector3 hips, out Vector3 wakiPos, out Quaternion wakiRot, out float hold)
        {
            Sample(move, rigIndex, t, bones, out hips, out wakiPos, out wakiRot, out hold, out _, out _);
        }

        // the same, and the wakizashi's scabbard too
        internal static void Sample(DaishoMove move, int rigIndex, float t, Quaternion[] bones, out Vector3 hips, out Vector3 wakiPos, out Quaternion wakiRot, out float hold, out Vector3 sayaPos, out Quaternion sayaRot)
        {
            var rig = move.Rigs[rigIndex];
            float x = Mathf.Clamp(t * Rate, 0f, move.Frames - 1);
            int a = Mathf.Min((int)x, move.Frames - 1);
            int b = Mathf.Min(a + 1, move.Frames - 1);
            float k = x - a;
            if (rig.Snap[b] && b != a) k = 0f;
            for (int i = 0; i < BoneCount; i++)
                bones[i] = Blend(Q(rig.Bones, a * BoneCount + i), Q(rig.Bones, b * BoneCount + i), k);
            hips = Vector3.Lerp(V(rig.Hips, a * 3), V(rig.Hips, b * 3), k);
            wakiPos = Vector3.Lerp(V(rig.Waki, a * 7), V(rig.Waki, b * 7), k);
            wakiRot = Blend(Q(rig.Waki, a * 7 + 3, true), Q(rig.Waki, b * 7 + 3, true), k);
            hold = Mathf.Lerp(rig.Hold[a], rig.Hold[b], k);
            sayaPos = Vector3.Lerp(V(rig.Saya, a * 7), V(rig.Saya, b * 7), k);
            sayaRot = Blend(Q(rig.Saya, a * 7 + 3, true), Q(rig.Saya, b * 7 + 3, true), k);
        }

        // the katana's grip place and attach rotation at the move's last frame, where the sheathing takes over
        internal static void KatanaEnd(DaishoMove move, int rigIndex, out Vector3 grip, out Quaternion rot)
        {
            var rig = move.Rigs[rigIndex];
            int f = move.Frames - 1;
            grip = V(rig.Kata, f * 7);
            rot = Q(rig.Kata, f * 7 + 3, true);
        }

        // both blade tips at time t in the player's frame (from the packed sweep)
        internal static void Tips(DaishoMove move, int rigIndex, float t, out Vector3 katana, out Vector3 wakizashi)
        {
            var rig = move.Rigs[rigIndex];
            float x = Mathf.Clamp(t * Rate, 0f, move.Frames - 1);
            int a = Mathf.Min((int)x, move.Frames - 1);
            int b = Mathf.Min(a + 1, move.Frames - 1);
            float k = rig.Snap[b] && b != a ? 0f : x - a;
            katana = Vector3.Lerp(V(rig.Tips, a * 6), V(rig.Tips, b * 6), k);
            wakizashi = Vector3.Lerp(V(rig.Tips, a * 6 + 3), V(rig.Tips, b * 6 + 3), k);
        }

        private static Quaternion Q(float[] data, int index, bool raw = false)
        {
            int o = raw ? index : index * 4;
            return new Quaternion(data[o], data[o + 1], data[o + 2], data[o + 3]);
        }

        private static Vector3 V(float[] data, int offset) => new Vector3(data[offset], data[offset + 1], data[offset + 2]);

        private static Quaternion Blend(Quaternion a, Quaternion b, float k)
        {
            if (k <= 0f) return a;
            if (k >= 1f) return b;
            if (a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w < 0f) b = new Quaternion(-b.x, -b.y, -b.z, -b.w);
            var q = new Quaternion(a.x + (b.x - a.x) * k, a.y + (b.y - a.y) * k, a.z + (b.z - a.z) * k, a.w + (b.w - a.w) * k);
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return m < 1e-6f ? a : new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
        }
    }
}
