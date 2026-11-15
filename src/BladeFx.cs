using System.Collections.Generic;
using UnityEngine;

namespace Iaido_Katana
{
    // a blade's own shape, measured from its mesh while it loads: its middle line from the guard to the tip, half its
    // width there, and a flat strip over it that particles are born on
    internal sealed class BladeShape
    {
        internal Vector3[] Spine;
        internal float[] Half;
        internal Mesh Strip;
    }

    // effects that lie on a blade's own curve instead of a straight line beside it (the owner, 15 Nov: the red lightning
    // sat behind the katana, the flames ran past the wakizashi's tip). only unity's own types here: the offline render
    // (devtools/session_2026-11-15_fx/UnityFx) compiles this same file and shows the same effect
    internal static class BladeFx
    {
        // all the numbers of the two effects, in one place
        internal const int SpineSlices = 16;          // along the blade
        internal const float StripShare = 0.7f;       // of the blade's width that particles are born on
        // the thunderblood's red lightning: thin jagged lines along the blade's middle
        internal const int ArcLines = 7;              // both katanas (the owner, 16 Nov: 7 looked better than 3)
        internal const int ForkLines = 4;             // short branches, each on or off by ForkChance
        internal const int ArcSteps = 3;              // points between two spine points
        internal const float ArcWidth = 0.011f;       // meters across a line
        internal const float ArcAcross = 0.8f;        // how far a line strays over the blade's flat, share of its half width
        internal const float ArcOff = 0.008f;         // and off its flat, meters
        internal const float ArcSeconds = 0.07f;      // between two shapes of the lines
        internal const float ForkLength = 0.095f;     // a short branch jumping off the blade
        internal const float ForkChance = 0.6f;
        // the first line runs the whole blade. each other one covers a stretch this long (share of the blade), and the
        // stretches and the forks are spread from the guard to the tip (the owner, 16 Nov: most of it sat mid-blade)
        internal const float ArcStretchMin = 0.3f, ArcStretchMax = 0.45f;
        internal const float ArcEnds = 0.2f;          // share of a line over which its ends come back to the middle line
        internal static readonly Color ArcHot = new Color(1f, 0.62f, 0.5f, 1f), ArcDeep = new Color(1f, 0.17f, 0f, 1f);
        // the flametal's blue lightning, the same lines in the slayer's colours (its light's blue)
        internal static readonly Color StormHot = new Color(0.86f, 0.96f, 1f, 1f), StormDeep = new Color(0.48f, 0.8f, 1f, 1f);
        // it has no sparks of its own, so its lines are larger and brighter (the owner, 15 and 16 Nov: too subtle)
        internal const float StormSize = 1.56f, StormBright = 1.3f;
        // the colour settings a lightning material may have, each made brighter
        private static readonly string[] BrightColours = { "_Color", "_TintColor", "_EmissionColor" };
        internal const float SparkSize = 0.68f, SparkRate = 3f;     // the game's red sparks, times these
        internal const float LightningLight = 0.5f;   // the game's red light, range and strength times this
        // the wakizashi's frostfire: the game's cyan flames, smaller, lower and more see-through
        internal const float FlameSize = 0.18f, FlameRise = 0.22f, FlameAlpha = 0.35f, FlameRate = 1.1f;
        internal const float GlowSize = 0.21f, GlowAlpha = 0.3f, GlowRate = 0.5f;
        internal const float FrostLight = 0.35f;

        // the blade's shape in its mesh frame (the blade along +y from `from`, curving in x, flat in z)
        internal static BladeShape Shape(List<Vector3> v, float from)
        {
            float tip = float.MinValue;
            foreach (var p in v) tip = Mathf.Max(tip, p.y);
            var spine = new List<Vector3>();
            var half = new List<float>();
            float step = (tip - from) / SpineSlices;
            for (int i = 0; i < SpineSlices; i++)
            {
                float low = from + i * step, high = low + step, least = float.MaxValue, most = float.MinValue;
                foreach (var p in v)
                    if (p.y >= low && p.y <= high)
                    {
                        least = Mathf.Min(least, p.x);
                        most = Mathf.Max(most, p.x);
                    }
                if (most < least) continue;
                spine.Add(new Vector3((least + most) * 0.5f, low + step * 0.5f, 0f));
                half.Add((most - least) * 0.5f);
            }
            // the tip itself
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (var p in v)
                if (p.y > tip - 0.004f)
                {
                    sum += p;
                    count++;
                }
            if (count > 0)
            {
                spine.Add(new Vector3(sum.x / count, tip, 0f));
                half.Add(0f);
            }
            return new BladeShape { Spine = spine.ToArray(), Half = half.ToArray() };
        }

        // a flat strip over the blade, kept readable (a particle system reads the mesh it is born on). made once, when
        // the first effect asks for it
        private static Mesh Strip(BladeShape shape)
        {
            if (shape.Strip) return shape.Strip;
            int n = shape.Spine.Length;
            var vertices = new Vector3[n * 2];
            var triangles = new int[Mathf.Max(0, n - 1) * 6];
            for (int i = 0; i < n; i++)
            {
                var across = new Vector3(Mathf.Max(0.002f, shape.Half[i] * StripShare), 0f, 0f);
                vertices[i * 2] = shape.Spine[i] - across;
                vertices[i * 2 + 1] = shape.Spine[i] + across;
            }
            for (int i = 0, k = 0; i < n - 1; i++)
            {
                int a = i * 2;
                triangles[k++] = a; triangles[k++] = a + 2; triangles[k++] = a + 1;
                triangles[k++] = a + 1; triangles[k++] = a + 2; triangles[k++] = a + 3;
            }
            var mesh = new Mesh { name = "IaidoKatana_blade_strip", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            shape.Strip = mesh;
            return mesh;
        }

        // the point at u (0 the guard, 1 the tip) of the middle line, and half the blade's width there
        internal static Vector3 At(Vector3[] spine, float[] half, float u, out float width)
        {
            float f = Mathf.Clamp01(u) * (spine.Length - 1);
            int i = Mathf.Min((int)f, spine.Length - 2);
            width = Mathf.Lerp(half[i], half[i + 1], f - i);
            return Vector3.Lerp(spine[i], spine[i + 1], f - i);
        }

        // a katana's lightning on its own node: jagged lines over the blade drawn with the game's lightning material in
        // the two colours, the game's sparks born on the blade where it has them, and its light. vfx is the bloodgold
        // greatsword's attach/VFX (the thunderblood's red) or the slayer's attach/default/VFX (the flametal's blue)
        internal const string ForkName = "fork";

        // size makes the lines wider and lets them stray further, bright makes the lines and the light stronger
        internal static GameObject Lightning(Transform node, BladeShape blade, Transform vfx, string name, Color hot, Color deep,
            float size = 1f, float bright = 1f)
        {
            int layer = node.gameObject.layer;
            var root = new GameObject(name) { layer = layer };
            root.SetActive(false);
            root.transform.SetParent(node, false);
            var source = vfx.Find("Lightning");
            var drawn = source ? source.GetComponent<ParticleSystemRenderer>() : null;
            var material = drawn ? drawn.trailMaterial : null;
            if (material)
            {
                var arcs = root.AddComponent<BladeArcs>();
                arcs.Spine = blade.Spine;
                arcs.Half = blade.Half;
                arcs.Size = size;
                if (bright != 1f)
                {
                    material = new Material(material);
                    foreach (string colour in BrightColours)
                        if (material.HasProperty(colour))
                        {
                            var c = material.GetColor(colour);
                            material.SetColor(colour, new Color(c.r * bright, c.g * bright, c.b * bright, c.a));
                        }
                }
                for (int i = 0; i < ArcLines + ForkLines; i++)
                {
                    bool fork = i >= ArcLines;
                    var line = new GameObject(fork ? ForkName : "arc", typeof(LineRenderer)) { layer = layer };
                    line.transform.SetParent(root.transform, false);
                    var l = line.GetComponent<LineRenderer>();
                    l.sharedMaterial = material;
                    l.useWorldSpace = false;
                    l.textureMode = LineTextureMode.Stretch;
                    l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    l.receiveShadows = false;
                    // the first line is the hot core, finer than the red one over it
                    l.widthMultiplier = (fork || i == 0 ? ArcWidth * 0.6f : ArcWidth) * size;
                    l.numCornerVertices = 2;
                    // thin at both ends
                    l.widthCurve = new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.15f, 1f), new Keyframe(0.85f, 1f), new Keyframe(1f, 0.1f));
                    l.startColor = i == 0 ? hot : deep;
                    l.endColor = i == 0 ? hot : deep;
                }
                arcs.Roll();
            }
            var sparks = vfx.Find("Sparcs");
            if (sparks)
            {
                var ps = OnBlade(sparks, root.transform, blade, layer);
                var main = ps.main;
                main.startSizeMultiplier *= SparkSize;
                var emission = ps.emission;
                emission.rateOverTimeMultiplier *= SparkRate;
            }
            Lamp(vfx.Find("Point light"), root.transform, blade, layer, LightningLight * bright);
            root.SetActive(true);
            return root;
        }

        // the wakizashi's frostfire on its own node: the game's cyan flames and their glow born on the blade, and its
        // light. burny is the frostfire greatsword's attach/Krom/Burny vfx
        internal static GameObject Flames(Transform node, BladeShape blade, Transform burny, string name)
        {
            int layer = node.gameObject.layer;
            var root = new GameObject(name) { layer = layer };
            root.SetActive(false);
            root.transform.SetParent(node, false);
            var flames = burny.Find("flames");
            if (flames)
            {
                var ps = OnBlade(flames, root.transform, blade, layer);
                var main = ps.main;
                main.startSizeMultiplier *= FlameSize;
                main.startColor = Dim(main.startColor, FlameAlpha);
                var emission = ps.emission;
                emission.rateOverTimeMultiplier *= FlameRate;
                var velocity = ps.velocityOverLifetime;
                velocity.yMultiplier *= FlameRise;
            }
            var glow = burny.Find("fx_Torch_Basic");
            if (glow)
            {
                var ps = OnBlade(glow, root.transform, blade, layer);
                var main = ps.main;
                main.startSizeMultiplier *= GlowSize;
                main.startColor = Dim(main.startColor, GlowAlpha);
                var emission = ps.emission;
                emission.rateOverTimeMultiplier *= GlowRate;
            }
            Lamp(burny.Find("Point light"), root.transform, blade, layer, FrostLight);
            root.SetActive(true);
            return root;
        }

        // a copy of one of the game's particle parts in the blade's mesh frame, born on the blade's strip
        private static ParticleSystem OnBlade(Transform part, Transform root, BladeShape blade, int layer)
        {
            var copy = Object.Instantiate(part.gameObject, root, false);
            copy.name = part.name;
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;
            foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            copy.SetActive(true);
            var ps = copy.GetComponent<ParticleSystem>();
            var main = ps.main;
            main.prewarm = main.loop;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Mesh;
            shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
            shape.mesh = Strip(blade);
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
            shape.scale = Vector3.one;
            shape.normalOffset = 0f;
            return ps;
        }

        // a copy of the game's light at the blade's middle, dimmer
        private static void Lamp(Transform part, Transform root, BladeShape blade, int layer, float share)
        {
            if (!part) return;
            var copy = Object.Instantiate(part.gameObject, root, false);
            copy.name = part.name;
            copy.layer = layer;
            copy.transform.localPosition = At(blade.Spine, blade.Half, 0.5f, out _);
            copy.transform.localScale = Vector3.one;
            copy.SetActive(true);
            foreach (var light in copy.GetComponentsInChildren<Light>(true))
            {
                light.range *= share;
                light.intensity *= share;
            }
        }

        private static ParticleSystem.MinMaxGradient Dim(ParticleSystem.MinMaxGradient g, float k)
        {
            Color C(Color c) => new Color(c.r, c.g, c.b, c.a * k);
            Gradient G(Gradient from)
            {
                if (from == null) return null;
                var keys = from.alphaKeys;
                for (int i = 0; i < keys.Length; i++) keys[i].alpha *= k;
                var to = new Gradient { mode = from.mode };
                to.SetKeys(from.colorKeys, keys);
                return to;
            }
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(C(g.color));
                case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(C(g.colorMin), C(g.colorMax));
                case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(G(g.gradientMin), G(g.gradientMax));
                default: return new ParticleSystem.MinMaxGradient(G(g.gradient)) { mode = g.mode };
            }
        }
    }

    // the lightning's lines: each runs along a stretch of the blade's middle line and takes a new jagged shape
    // several times a second. the lines named ForkName are short forks that jump off the blade now and then. the
    // stretches and the forks each take their own part of the blade, so the whole blade is covered
    internal sealed class BladeArcs : MonoBehaviour
    {
        public Vector3[] Spine;
        public float[] Half;
        public float Size = 1f;
        private LineRenderer[] _lines;
        private Vector3[] _points;
        private float _next;

        private void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + BladeFx.ArcSeconds;
            Roll();
        }

        internal void Roll()
        {
            if (Spine == null || Spine.Length < 2) return;
            if (_lines == null || _lines.Length == 0) _lines = GetComponentsInChildren<LineRenderer>(true);
            int arcs = 0, forks = 0;
            foreach (var line in _lines)
                if (line.name == BladeFx.ForkName) forks++;
                else arcs++;
            int arc = 0, fork = 0;
            for (int i = 0; i < _lines.Length; i++)
            {
                if (_lines[i].name == BladeFx.ForkName) Fork(_lines[i], fork++, forks);
                else Arc(_lines[i], arc++, arcs);
            }
        }

        // line 0 runs the whole blade, line k of the others a stretch around its own part of it
        private void Arc(LineRenderer line, int k, int count)
        {
            int n = (Spine.Length - 1) * BladeFx.ArcSteps + 1;
            if (_points == null || _points.Length != n) _points = new Vector3[n];
            float from = Random.Range(0f, 0.05f), to = Random.Range(0.95f, 1f);
            if (k > 0 && count > 1)
            {
                float length = Random.Range(BladeFx.ArcStretchMin, BladeFx.ArcStretchMax);
                float middle = (k - 1 + Random.value) / (count - 1);
                from = Mathf.Clamp(middle - length * 0.5f, 0f, 1f - length);
                to = from + length;
            }
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)(n - 1);
                var at = BladeFx.At(Spine, Half, Mathf.Lerp(from, to, u), out float half);
                float held = Mathf.Clamp01(Mathf.Min(u, 1f - u) / BladeFx.ArcEnds); // the ends stay on the middle line
                _points[i] = at + new Vector3(Random.Range(-1f, 1f) * half * BladeFx.ArcAcross, 0f, Random.Range(-1f, 1f) * BladeFx.ArcOff) * (held * Size);
            }
            line.positionCount = n;
            line.SetPositions(_points);
        }

        // fork k jumps off its own part of the blade
        private void Fork(LineRenderer line, int k, int count)
        {
            line.enabled = Random.value < BladeFx.ForkChance;
            if (!line.enabled) return;
            var at = BladeFx.At(Spine, Half, Mathf.Lerp(0.05f, 0.97f, (k + Random.value) / Mathf.Max(1, count)), out _);
            var way = new Vector3(Random.value < 0.5f ? -1f : 1f, Random.Range(0.2f, 0.9f), Random.Range(-0.4f, 0.4f)).normalized;
            line.positionCount = 4;
            for (int i = 0; i < 4; i++)
            {
                var jag = i == 0 ? Vector3.zero : new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * (BladeFx.ForkLength * Size * 0.15f);
                line.SetPosition(i, at + way * (BladeFx.ForkLength * Size * i / 3f) + jag);
            }
        }
    }
}
