using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Iaido_Katana
{
    // small looks around the iai cut, from the two inspiration clips: a glint at the scabbard mouth when the parry lands,
    // a coloured trail on the cut, and lightning crackling at the flametal scabbard's mouth. all built from the game's own
    // materials and particles, made on every client that sees the player
    internal static class KatanaFx
    {
        private static readonly AccessTools.FieldRef<MeleeWeaponTrail, Material> TrailMaterial = AccessTools.FieldRefAccess<MeleeWeaponTrail, Material>("_material");

        // the cut's trail: gold for steel, blue white lightning for flametal, fading over the trail's life
        internal static readonly Color[] SteelTrail = { new Color(1f, 0.82f, 0.35f, 1f), new Color(1f, 0.6f, 0.15f, 0.6f), new Color(1f, 0.5f, 0.1f, 0f) };
        internal static readonly Color[] FlametalTrail = { new Color(0.85f, 0.95f, 1f, 1f), new Color(0.35f, 0.6f, 1f, 0.7f), new Color(0.2f, 0.35f, 1f, 0f) };

        private const float GlintSeconds = 0.16f;
        private const float GlintSize = 0.7f;
        private static Material _glintMaterial;
        private static Mesh _quad;

        // the game's own swing trail material (krom's), for the katana's trail and the glint
        internal static Material TrailMaterialOf(string prefab)
        {
            var item = Jotunn.Managers.PrefabManager.Instance.GetPrefab(prefab);
            var trail = item ? item.GetComponentInChildren<MeleeWeaponTrail>(true) : null;
            return trail ? TrailMaterial(trail) : null;
        }

        private const float SparkLife = 2f; // the sparks after the impact frame live this much longer

        // the weapon's hit effect at a counter or a thunderclap hit. its sparks are a network effect, so every player sees
        // them at the hit. under this client's own impact frame (hiddenFor seconds) they would burn out unseen: here they
        // are cleared, and a local copy that lives twice as long flies when the frame ends. the other players see the
        // game's sparks once, at the hit, as always
        internal static void HitEffect(EffectList effects, Vector3 point, Quaternion rot, float hiddenFor)
        {
            var made = effects.Create(point, rot);
            if (hiddenFor <= 0f || made == null || !Plugin.Instance) return;
            foreach (var data in effects.m_effectPrefabs)
            {
                var prefab = data.m_enabled ? data.m_prefab : null;
                if (!prefab || !prefab.GetComponentInChildren<ParticleSystem>(true)) continue;
                foreach (var go in made)
                    if (go && go.name.StartsWith(prefab.name))
                        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                Plugin.Instance.StartCoroutine(SparksAfter(prefab, point, rot, hiddenFor));
            }
        }

        private static IEnumerator SparksAfter(GameObject prefab, Vector3 point, Quaternion rot, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            GameObject copy;
            ZNetView.m_forceDisableInit = true; // made here only, never sent to the other players
            try
            {
                copy = Object.Instantiate(prefab, point, rot);
            }
            finally
            {
                ZNetView.m_forceDisableInit = false;
            }
            foreach (var ps in copy.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.startLifetimeMultiplier *= SparkLife;
                ps.Play(false);
            }
            Object.Destroy(copy, 6f);
        }

        // a white star at the scabbard mouth with a flash of light, held still for a moment
        internal static void Glint(Vector3 point)
        {
            if (!Plugin.GlintOn.Value || !Plugin.Instance) return;
            var material = GlintMaterial();
            if (!material) return;
            var star = new GameObject("IaidoKatana_glint", typeof(MeshFilter), typeof(MeshRenderer));
            star.transform.position = point;
            star.GetComponent<MeshFilter>().sharedMesh = Quad();
            star.GetComponent<MeshRenderer>().sharedMaterial = material;
            var light = star.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.95f, 0.85f);
            light.range = 4f;
            light.intensity = 0f;
            Plugin.Instance.StartCoroutine(PlayGlint(star, light));
        }

        private static IEnumerator PlayGlint(GameObject star, Light light)
        {
            float start = Time.time;
            while (star)
            {
                float t = (Time.time - start) / GlintSeconds;
                if (t >= 1f) break;
                var camera = Camera.main;
                if (camera) star.transform.rotation = camera.transform.rotation * Quaternion.Euler(0f, 0f, 45f * t);
                float size = Mathf.Sin(Mathf.PI * Mathf.Sqrt(t)) * GlintSize; // up fast, down slower
                star.transform.localScale = new Vector3(size, size, size);
                light.intensity = 5f * (1f - t);
                yield return null;
            }
            if (star) Object.Destroy(star);
        }

        // a full-body cut's whoosh as its strike begins, heard around the player: a sword's swing played quick and high,
        // and a knife's under it for the edge. the game's own swing sound still comes with the greatsword swing's trail
        private const string SlashHigh = "sfx_sword_swing", SlashEdge = "sfx_knife_swing";

        internal static void SlashSound(Vector3 point, bool flametal)
        {
            float volume = Plugin.SlashSoundVolume.Value;
            if (volume <= 0f) return;
            ImpactFrame.PlayLayer(point, SlashHigh, flametal ? 1.15f : 1.25f, volume, 0f, true);
            ImpactFrame.PlayLayer(point, SlashEdge, 0.9f, volume * 0.7f, 0f, true);
        }

        // where a full-body cut lands, on the hitting player's own screen: a thin straight line of light along the cut
        // through the blade, held bright for a moment and then fading slowly (the inspiration clip's golden slash), a
        // flash of light and a small camera jolt
        private const float MarkHold = 0.12f, MarkFade = 0.6f, MarkGrow = 0.04f, MarkLength = 2.6f, MarkWidth = 0.07f;
        private static Material _markMaterial;

        internal static void SlashMark(Vector3 point, Vector3 along, bool flametal)
        {
            if (!Plugin.SlashMarkOn.Value || !Plugin.Instance) return;
            var material = MarkMaterial();
            if (!material || along == Vector3.zero) return;
            var mark = new GameObject("IaidoKatana_slashmark", typeof(MeshFilter), typeof(MeshRenderer));
            mark.transform.position = point;
            var mesh = new Mesh { name = "IaidoKatana_slashmark" };
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            mark.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = mark.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var light = mark.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = flametal ? new Color(0.7f, 0.85f, 1f) : new Color(1f, 0.85f, 0.55f);
            light.range = 5f;
            light.intensity = 0f;
            var colour = flametal ? FlametalTrail[0] : SteelTrail[0];
            Plugin.Instance.StartCoroutine(PlayMark(mark, mesh, light, along.normalized, colour));
            var camera = GameCamera.instance;
            if (camera && Plugin.SlashShake.Value > 0f) camera.AddShake(camera.transform.position, 20f, Plugin.SlashShake.Value, false);
        }

        private static IEnumerator PlayMark(GameObject mark, Mesh mesh, Light light, Vector3 along, Color colour)
        {
            float start = Time.time;
            var colours = new Color[4];
            while (mark)
            {
                float t = Time.time - start;
                if (t >= MarkHold + MarkFade) break;
                // the line lies along the cut, its face turned to the camera
                var camera = Camera.main;
                if (camera)
                {
                    var toCamera = camera.transform.position - mark.transform.position;
                    var face = toCamera - along * Vector3.Dot(toCamera, along);
                    if (face.sqrMagnitude > 1e-6f) mark.transform.rotation = Quaternion.LookRotation(-face, along);
                }
                float grow = Mathf.Clamp01(t / MarkGrow);
                float fade = t < MarkHold ? 1f : 1f - (t - MarkHold) / MarkFade;
                fade *= fade;
                mark.transform.localScale = new Vector3(MarkWidth * (1f + 0.5f * (1f - fade)), MarkLength * (0.4f + 0.6f * grow), 1f);
                var c = Color.Lerp(new Color(1f, 1f, 1f, 1f), colour, Mathf.Clamp01(t / MarkHold)); // white hot first
                c.a = fade;
                for (int i = 0; i < 4; i++) colours[i] = c;
                mesh.colors = colours;
                light.intensity = 4f * fade * (t < MarkHold ? 1f : 0.5f);
                yield return null;
            }
            if (mark) Object.Destroy(mark);
            Object.Destroy(mesh);
        }

        // the trail's own additive material with a thin line drawn into it, bright in the middle, sharp at both ends
        private static Material MarkMaterial()
        {
            if (_markMaterial) return _markMaterial;
            var template = TrailMaterialOf("THSwordKrom");
            if (!template)
            {
                Plugin.Log.LogWarning("slash mark: the trail material was not found, no mark");
                return null;
            }
            _markMaterial = new Material(template) { name = "IaidoKatana_slashmark", mainTexture = LineTexture() };
            if (_markMaterial.HasProperty("_TintColor")) _markMaterial.SetColor("_TintColor", new Color(0.6f, 0.58f, 0.55f, 0.5f));
            return _markMaterial;
        }

        private static Texture2D LineTexture()
        {
            const int w = 16, h = 128;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "IaidoKatana_line" };
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
                    float taper = Mathf.Clamp01(1f - Mathf.Abs(v)); // widest in the middle, to a point at both ends
                    float across = Mathf.Abs(u) / Mathf.Max(0.05f, taper);
                    float core = Mathf.Exp(-across * across * 6f);
                    byte a = (byte)(255f * Mathf.Clamp01(core * Mathf.Sqrt(taper)));
                    pixels[y * w + x] = new Color32(a, a, a, a);
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        // the trail's own additive material with a four pointed star drawn into it
        private static Material GlintMaterial()
        {
            if (_glintMaterial) return _glintMaterial;
            var template = TrailMaterialOf("THSwordKrom");
            if (!template)
            {
                Plugin.Log.LogWarning("parry glint: the trail material was not found, no glint");
                return null;
            }
            _glintMaterial = new Material(template) { name = "IaidoKatana_glint", mainTexture = StarTexture() };
            if (_glintMaterial.HasProperty("_TintColor")) _glintMaterial.SetColor("_TintColor", new Color(0.6f, 0.58f, 0.55f, 0.5f));
            return _glintMaterial;
        }

        private static Texture2D StarTexture()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "IaidoKatana_star" };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float r2 = u * u + v * v;
                    float core = Mathf.Exp(-r2 * 30f);
                    float rays = Mathf.Exp(-Mathf.Abs(u) * 40f) * Mathf.Exp(-Mathf.Abs(v) * 2.5f) + Mathf.Exp(-Mathf.Abs(v) * 40f) * Mathf.Exp(-Mathf.Abs(u) * 2.5f);
                    float edge = Mathf.Clamp01((1f - Mathf.Sqrt(r2)) * 4f); // nothing at the quad's border
                    byte a = (byte)(255f * Mathf.Clamp01((core + rays * 0.9f) * edge));
                    pixels[y * size + x] = new Color32(a, a, a, a);
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static Mesh Quad()
        {
            if (_quad) return _quad;
            _quad = new Mesh { name = "IaidoKatana_quad" };
            _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _quad.RecalculateBounds();
            return _quad;
        }

        // the slayer lightning's crackle, made small and short, at a point under the given parent. off until enabled
        internal static ParticleSystem Sparks(Transform parent, Vector3 local)
        {
            var ps = LightningCopy(parent, "IaidoKatana_sparks", local, "flametal sparks");
            if (!ps) return null;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.04f;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            ps.Play();
            return ps;
        }

        internal const string LightningName = "IaidoKatana_lightning";
        // the slayer's lightning is laid along a straight blade; turned this far toward the back (-x of the attach
        // frame) about where the katana's blade leaves the guard, it lies along the line from there to the curved tip
        private const float CurveTurn = -7.6f;
        private static readonly Vector3 CurvePivot = new Vector3(0f, 0f, 0.14f);

        // the flametal blade's lightning while it is drawn, the way the game's iolite weapons have it (slayer, splitnir,
        // nidhogg): a glow shell over the blade in the game's WeaponGlow material, arcs flowing along the blade joined
        // into three jagged ribbons, faint storm wisps and a flickering blue light. the slayer's own parts with its own
        // settings, laid along the katana. made once on the item prefab, so every copy has it (in the hand, on the back,
        // dropped); IaiStance switches it off while the blade is in its scabbard
        internal static void BladeLightning(Transform node, Mesh glowMesh)
        {
            var source = Jotunn.Managers.PrefabManager.Instance.GetPrefab("THSwordSlayerLightning");
            var vfx = source ? source.transform.Find("attach/default/VFX") : null;
            if (!vfx)
            {
                Plugin.Log.LogWarning("flametal blade lightning: the slayer's lightning was not found, none");
                return;
            }
            int layer = node.gameObject.layer;
            var root = new GameObject(LightningName) { layer = layer };
            root.transform.SetParent(node, false);

            // the glow shell, on the katana mesh's own frame
            var glowMaterial = vfx.GetComponent<MeshRenderer>() ? vfx.GetComponent<MeshRenderer>().sharedMaterial : null;
            if (glowMesh && glowMaterial)
            {
                var glow = new GameObject("glow", typeof(MeshFilter), typeof(MeshRenderer)) { layer = layer };
                glow.transform.SetParent(root.transform, false);
                glow.GetComponent<MeshFilter>().sharedMesh = glowMesh;
                var renderer = glow.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = glowMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            else Plugin.Log.LogWarning("flametal blade lightning: no glow shell");

            // the arcs, wisps and light where the slayer has them in its attach frame (the hand at 0, the blade along +z,
            // the same frame the katana is held in), turned at the guard along the katana's curve
            var arcs = new GameObject("arcs") { layer = layer };
            arcs.transform.SetParent(root.transform, false);
            var slayerBlade = vfx.parent;
            var inAttach = slayerBlade.localPosition + slayerBlade.localRotation * vfx.localPosition;
            var turnInAttach = slayerBlade.localRotation * vfx.localRotation;
            var curve = Quaternion.Euler(0f, CurveTurn, 0f);
            var toNode = Quaternion.Inverse(KatanaModel.NodeRotation);
            arcs.transform.localPosition = toNode * (CurvePivot + curve * (inAttach - CurvePivot) - KatanaModel.NodePosition);
            arcs.transform.localRotation = toNode * curve * turnInAttach;
            foreach (Transform part in vfx)
            {
                var copy = Object.Instantiate(part.gameObject, arcs.transform, false);
                copy.name = part.name;
                foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                // the arcs are already there, not building up
                foreach (var ps in copy.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.prewarm = main.loop;
                    // the storm wisps 80% more see-through than the slayer's (the user found them too thick)
                    if (part.name == "Clouds") main.startColor = Faded(main.startColor, CloudAlpha);
                }
            }
        }

        private const float CloudAlpha = 0.2f;

        private static ParticleSystem.MinMaxGradient Faded(ParticleSystem.MinMaxGradient g, float k)
        {
            Color F(Color c) => new Color(c.r, c.g, c.b, c.a * k);
            Gradient G(Gradient gr)
            {
                if (gr == null) return null;
                var keys = gr.alphaKeys;
                for (int i = 0; i < keys.Length; i++) keys[i].alpha *= k;
                var faded = new Gradient { mode = gr.mode };
                faded.SetKeys(gr.colorKeys, keys);
                return faded;
            }
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(F(g.color));
                case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(F(g.colorMin), F(g.colorMax));
                case ParticleSystemGradientMode.Gradient: return new ParticleSystem.MinMaxGradient(G(g.gradient));
                case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(G(g.gradientMin), G(g.gradientMax));
                default:
                    var random = new ParticleSystem.MinMaxGradient(G(g.gradient)) { mode = ParticleSystemGradientMode.RandomColor };
                    return random;
            }
        }

        // a copy of an item's blade lightning under parent (the katana mesh's frame), for the katana put away at the hip
        internal static GameObject CopyLightning(string prefab, Transform parent)
        {
            var item = Jotunn.Managers.PrefabManager.Instance.GetPrefab(prefab);
            Transform source = null;
            if (item)
                foreach (var t in item.GetComponentsInChildren<Transform>(true))
                    if (t.name == LightningName) source = t;
            if (!source) return null;
            var copy = Object.Instantiate(source.gameObject, parent, false);
            copy.name = LightningName;
            foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = parent.gameObject.layer;
            return copy;
        }

        // the blade's lightning on or off: off while the blade is in its scabbard
        internal static void Lightning(GameObject root, bool on)
        {
            if (root && root.activeSelf != on) root.SetActive(on);
        }

        // a copy of the slayer lightning's particles (drawn as trails, so they look like arcs), short lived and fine
        private static ParticleSystem LightningCopy(Transform parent, string name, Vector3 local, string what)
        {
            var source = Jotunn.Managers.PrefabManager.Instance.GetPrefab("THSwordSlayerLightning");
            var crackle = source ? source.transform.Find("attach/default/VFX/Lightning") : null;
            if (!crackle || !crackle.GetComponent<ParticleSystem>())
            {
                Plugin.Log.LogWarning($"{what}: the slayer lightning particles were not found, none");
                return null;
            }
            var copy = Object.Instantiate(crackle.gameObject, parent, false);
            copy.name = name;
            copy.layer = parent.gameObject.layer;
            copy.transform.localPosition = local;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;
            var ps = copy.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f); // the blade's own crackle is finer than a hand
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.prewarm = false;
            var shape = ps.shape;
            shape.enabled = true;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
            return ps;
        }

        // how hard the sparks crackle: 0 stops them, 1 is a quiet crackle, more builds up
        internal static void SparkRate(ParticleSystem ps, float strength)
        {
            if (!ps) return;
            var emission = ps.emission;
            emission.rateOverTime = strength <= 0f ? 0f : 30f * strength;
            // the scabbard is switched off and on with the katana, which stops the particles
            if (strength > 0f && !ps.isPlaying) ps.Play();
        }
    }

    // the katana's own swing trail: a ribbon from the blade's base to its tip, sampled after the pose is written, so it
    // follows the cut that is shown. the game's trail on the katana is off, it sampled the greatsword swing under the
    // pose and drew long beams through the body. one per player that cuts, under the player, in world space
    internal sealed class BladeTrail
    {
        private const float Life = 0.22f;   // seconds a point of the ribbon lasts
        private const float MaxTurn = 6f;   // degrees between two points, more is filled in along the arc
        private const int MaxPoints = 120;
        private const float BaseAlong = 0.55f; // past the guard, along the katana mesh from the pommel
        // a full-body cut's ribbon (Bold): an anime slash, longer lived, a white hot edge out past the tip fading in
        // toward the guard, held bright for a moment before it fades
        private const float BoldLife = 0.32f, BoldHold = 0.3f, BoldMid = 0.7f, BoldTip = 1.06f, BoldWhite = 0.65f;
        private bool _bold;

        // on with a full-body cut, and kept until its ribbon has faded, so the points already drawn do not change look
        internal void Bold(bool on)
        {
            if (on) _bold = true;
            else if (_points.Count == 0) _bold = false;
        }

        private struct Point
        {
            internal Vector3 Pos;
            internal Quaternion Rot;
            internal float Scale, Time;
        }

        private static Material _material;
        private static bool _failed;
        private readonly GameObject _object;
        private readonly Mesh _mesh;
        private readonly MeshRenderer _renderer;
        private readonly List<Point> _points = new List<Point>();
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Color> _colours = new List<Color>();
        private readonly List<int> _triangles = new List<int>();

        private BladeTrail(Transform parent, int layer)
        {
            _object = new GameObject("IaidoKatana_trail", typeof(MeshFilter), typeof(MeshRenderer)) { layer = layer };
            _object.transform.SetParent(parent, false);
            _mesh = new Mesh { name = "IaidoKatana_trail" };
            _mesh.MarkDynamic();
            _object.GetComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = _object.GetComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.enabled = false;
        }

        internal static BladeTrail Make(Transform parent, int layer)
        {
            if (_failed) return null;
            if (!_material) _material = KatanaFx.TrailMaterialOf("THSwordKrom");
            if (!_material)
            {
                _failed = true;
                Plugin.Log.LogWarning("katana trail: the game's trail material was not found, no trail");
                return null;
            }
            return new BladeTrail(parent, layer);
        }

        // blade is the katana mesh in the hand, emit whether the cut is on now
        internal void Sample(Transform blade, bool emit, bool flametal)
        {
            if (!_object) return;
            float now = Time.time;
            if (emit)
            {
                var point = new Point { Pos = blade.position, Rot = blade.rotation, Scale = blade.lossyScale.x, Time = now };
                if (_points.Count > 0)
                {
                    // a fast cut turns far in one frame: points along the arc between, so the ribbon stays round
                    var last = _points[_points.Count - 1];
                    int extra = Mathf.Min(12, (int)(Quaternion.Angle(last.Rot, point.Rot) / MaxTurn));
                    for (int i = 1; i <= extra; i++)
                    {
                        float u = i / (extra + 1f);
                        _points.Add(new Point
                        {
                            Pos = Vector3.Lerp(last.Pos, point.Pos, u), Rot = Quaternion.Slerp(last.Rot, point.Rot, u),
                            Scale = point.Scale, Time = Mathf.Lerp(last.Time, now, u),
                        });
                    }
                }
                _points.Add(point);
            }
            float life = _bold ? BoldLife : Life;
            int old = 0;
            while (old < _points.Count && (now - _points[old].Time >= life || _points.Count - old > MaxPoints)) old++;
            if (old > 0) _points.RemoveRange(0, old);
            if (_points.Count < 2)
            {
                Hide();
                return;
            }

            var filter = blade.GetComponent<MeshFilter>();
            var bounds = filter && filter.sharedMesh ? filter.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.zero);
            var tipLocal = bounds.max.y > 1f ? new Vector3(bounds.max.x, bounds.max.y, 0f) : new Vector3(0.17f, 2.14f, 0f);
            var baseLocal = new Vector3(0f, BaseAlong, 0f);
            var stops = flametal ? KatanaFx.FlametalTrail : KatanaFx.SteelTrail;
            _vertices.Clear();
            _colours.Clear();
            _triangles.Clear();
            if (_bold) BoldRibbon(stops, baseLocal, tipLocal, now);
            else
                for (int i = 0; i < _points.Count; i++)
                {
                    var p = _points[i];
                    _vertices.Add(p.Pos + p.Rot * (baseLocal * p.Scale));
                    _vertices.Add(p.Pos + p.Rot * (tipLocal * p.Scale));
                    var colour = Stop(stops, (now - p.Time) / Life);
                    _colours.Add(colour);
                    _colours.Add(colour);
                    if (i == 0) continue;
                    int a = (i - 1) * 2;
                    _triangles.Add(a);
                    _triangles.Add(a + 1);
                    _triangles.Add(a + 2);
                    _triangles.Add(a + 2);
                    _triangles.Add(a + 1);
                    _triangles.Add(a + 3);
                }
            // the vertices are in the world, so the ribbon's own object sits at the world's origin
            _object.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(_colours);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.RecalculateBounds();
            _renderer.enabled = true;
        }

        // three rows along the blade: clear at the base, the trail's colour at BoldMid, white hot at the edge past the tip
        private void BoldRibbon(Color[] stops, Vector3 baseLocal, Vector3 tipLocal, float now)
        {
            var midLocal = baseLocal + (tipLocal - baseLocal) * BoldMid;
            var edgeLocal = baseLocal + (tipLocal - baseLocal) * BoldTip;
            for (int i = 0; i < _points.Count; i++)
            {
                var p = _points[i];
                _vertices.Add(p.Pos + p.Rot * (baseLocal * p.Scale));
                _vertices.Add(p.Pos + p.Rot * (midLocal * p.Scale));
                _vertices.Add(p.Pos + p.Rot * (edgeLocal * p.Scale));
                float age = (now - p.Time) / BoldLife;
                var colour = Stop(stops, Mathf.Clamp01((age - BoldHold) / (1f - BoldHold))); // held bright, then fading
                _colours.Add(new Color(colour.r, colour.g, colour.b, 0f));
                _colours.Add(colour);
                _colours.Add(Color.Lerp(colour, new Color(1f, 1f, 1f, colour.a), BoldWhite));
                if (i == 0) continue;
                int a = (i - 1) * 3;
                for (int row = 0; row < 2; row++)
                {
                    int r = a + row;
                    _triangles.Add(r);
                    _triangles.Add(r + 1);
                    _triangles.Add(r + 3);
                    _triangles.Add(r + 3);
                    _triangles.Add(r + 1);
                    _triangles.Add(r + 4);
                }
            }
        }

        // the colour at u (0 new, 1 at the end of its life) along the stops
        private static Color Stop(Color[] stops, float u)
        {
            float x = Mathf.Clamp01(u) * (stops.Length - 1);
            int i = Mathf.Min((int)x, stops.Length - 2);
            return Color.Lerp(stops[i], stops[i + 1], x - i);
        }

        private void Hide()
        {
            if (!_renderer || !_renderer.enabled) return;
            _renderer.enabled = false;
            _mesh.Clear();
        }

        internal void Clear()
        {
            _points.Clear();
            Hide();
        }
    }
}
