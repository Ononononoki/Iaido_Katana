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

        private const float GlintSeconds = 0.3f;
        private const float GlintSize = 1.4f;
        private const float GlintLightRange = 8f, GlintLightIntensity = 8f;
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
        // seconds above 0 sets how long it lasts. returns the star, null when none was made
        internal static GameObject Glint(Vector3 point, bool gold = false, float seconds = 0f)
        {
            if (!Plugin.GlintOn.Value || !Plugin.Instance) return null;
            var material = gold ? GoldGlintMaterial() : GlintMaterial();
            if (!material) return null;
            var star = new GameObject("IaidoKatana_glint", typeof(MeshFilter), typeof(MeshRenderer));
            star.transform.position = point;
            star.GetComponent<MeshFilter>().sharedMesh = Quad();
            star.GetComponent<MeshRenderer>().sharedMaterial = material;
            var light = star.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = gold ? GoldLight : new Color(1f, 0.95f, 0.85f);
            light.range = gold ? GoldGlintRange : GlintLightRange;
            light.intensity = 0f;
            float lasts = seconds > 0f ? seconds : gold ? GoldGlintSeconds : GlintSeconds;
            Plugin.Instance.StartCoroutine(PlayGlint(star, light, lasts, gold ? GoldGlintSize : GlintSize, gold ? GoldGlintLight : GlintLightIntensity));
            return star;
        }

        private static IEnumerator PlayGlint(GameObject star, Light light, float seconds, float glintSize, float intensity)
        {
            float start = Time.time;
            while (star)
            {
                float t = (Time.time - start) / seconds;
                if (t >= 1f) break;
                var camera = Camera.main;
                if (camera) star.transform.rotation = camera.transform.rotation * Quaternion.Euler(0f, 0f, 45f * t);
                float size = Mathf.Sin(Mathf.PI * Mathf.Sqrt(t)) * glintSize; // up fast, down slower
                star.transform.localScale = new Vector3(size, size, size);
                light.intensity = intensity * (1f - t);
                yield return null;
            }
            if (star) Object.Destroy(star);
        }

        // the steel heavy's glint climbing the blade: a small pale gold star with a light, moved by IaiStance along the blade.
        // its lights stay low so the heavy does not light up dark places
        private static readonly Color GoldLight = new Color(1f, 0.88f, 0.62f);
        private const float ClimbSize = 0.55f, ClimbLight = 2.5f, ClimbLightRange = 3f;
        // the gold shine at the tip is bigger and longer than the parry's glint
        private const float GoldGlintSize = 2.4f, GoldGlintSeconds = 0.45f, GoldGlintLight = 6f, GoldGlintRange = 6f;
        private static readonly Color GoldGlintTint = new Color(1f, 0.88f, 0.62f, 0.65f);
        private static Material _goldGlintMaterial;

        internal static Transform ClimbingGlint()
        {
            if (!Plugin.GlintOn.Value) return null;
            var material = GoldGlintMaterial();
            if (!material) return null;
            var star = new GameObject("IaidoKatana_climbingglint", typeof(MeshFilter), typeof(MeshRenderer));
            star.GetComponent<MeshFilter>().sharedMesh = Quad();
            star.GetComponent<MeshRenderer>().sharedMaterial = material;
            star.transform.localScale = Vector3.one * ClimbSize;
            var light = star.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = GoldLight;
            light.range = ClimbLightRange;
            light.intensity = ClimbLight;
            return star.transform;
        }

        // the climbing glint at a point, facing the camera and twinkling, its size and light scaled by strength (0 to 1)
        internal static void MoveGlint(Transform star, Vector3 point, float strength = 1f)
        {
            if (!star) return;
            star.position = point;
            var camera = Camera.main;
            if (camera) star.rotation = camera.transform.rotation * Quaternion.Euler(0f, 0f, 90f * Time.time);
            star.localScale = Vector3.one * ClimbSize * strength * (0.85f + 0.15f * Mathf.Sin(Time.time * 40f));
            var light = star.GetComponent<Light>();
            if (light) light.intensity = ClimbLight * strength;
        }

        private static Material GoldGlintMaterial()
        {
            if (_goldGlintMaterial) return _goldGlintMaterial;
            var white = GlintMaterial();
            if (!white) return null;
            _goldGlintMaterial = new Material(white) { name = "IaidoKatana_goldglint" };
            if (_goldGlintMaterial.HasProperty("_TintColor")) _goldGlintMaterial.SetColor("_TintColor", GoldGlintTint);
            return _goldGlintMaterial;
        }

        // the parry's cut as an afterimage: a wide flat crescent of light around the player at the height of the cut,
        // as far and as wide as the cut reaches (Counter.AreaRange, AreaAngle). it sweeps in from the left like the draw,
        // stays bright, then fades slowly. gold for steel, blue white for flametal with a lightning flicker on its edge.
        // made on every client from the parry's synced timeline, so friends with the mod see it too
        private const float ArcSweep = 0.06f, ArcHold = 0.25f, ArcFade = 0.9f, ArcHeight = 1.05f, ArcThick = 0.38f;
        private const int ArcSegments = 48;
        private static Material _arcMaterial;

        // the steel heavy's afterimage stands upright in front, from over the head down to the ground, as far as the blade
        private const float HeavyArcRange = 2.4f, HeavyArcAngle = 120f, HeavyArcHeight = 1.4f;
        // the parry arc lights 2.5 times its reach around it, the heavy's arc a quarter as bright and only about 3 m,
        // so the heavy does not flood the ground with light
        private const float ArcLight = 6f, ArcLightReach = 2.5f, HeavyArcLight = 1.5f, HeavyArcLightRange = 3f;

        internal static void SlashArc(Transform body, bool flametal, bool parry = true, bool upright = false)
        {
            if ((parry || upright) && !Plugin.AfterimageOn.Value || !Plugin.Instance || !body) return;
            var material = ArcMaterial();
            if (!material) return;
            var ahead = body.forward;
            ahead.y = 0f;
            if (ahead.sqrMagnitude < 1e-4f) return;
            var arc = new GameObject("IaidoKatana_afterimage", typeof(MeshFilter), typeof(MeshRenderer));
            arc.transform.position = body.position + Vector3.up * (upright ? HeavyArcHeight : ArcHeight);
            // upright: the crescent turned on its side, so it sweeps in from above and comes down
            arc.transform.rotation = Quaternion.LookRotation(ahead.normalized, Vector3.up) * (upright ? Quaternion.Euler(0f, 0f, -90f) : Quaternion.identity);
            var mesh = new Mesh { name = "IaidoKatana_afterimage" };
            arc.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = arc.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var light = arc.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = flametal ? new Color(0.65f, 0.82f, 1f) : new Color(1f, 0.82f, 0.45f);
            float range = upright ? HeavyArcRange : Plugin.CounterAreaRange.Value;
            light.range = upright ? HeavyArcLightRange : range * ArcLightReach;
            light.intensity = 0f;
            Plugin.Instance.StartCoroutine(PlayArc(arc, mesh, light, range, upright ? HeavyArcAngle : Plugin.CounterAreaAngle.Value, flametal,
                upright ? HeavyArcLight : ArcLight));
        }

        private static IEnumerator PlayArc(GameObject arc, Mesh mesh, Light light, float range, float angle, bool flametal, float peakLight)
        {
            int n = ArcSegments + 1;
            var vertices = new Vector3[n * 2];
            var colours = new Color[n * 2];
            var triangles = new int[ArcSegments * 12];
            for (int i = 0, k = 0; i < ArcSegments; i++)
            {
                int o = i * 2, p = o + 2;
                // both faces, so it shows from above and below
                triangles[k++] = o; triangles[k++] = p; triangles[k++] = o + 1;
                triangles[k++] = o + 1; triangles[k++] = p; triangles[k++] = p + 1;
                triangles[k++] = o; triangles[k++] = o + 1; triangles[k++] = p;
                triangles[k++] = o + 1; triangles[k++] = p + 1; triangles[k++] = p;
            }
            var colour = flametal ? FlametalTrail[0] : SteelTrail[0];
            var deep = flametal ? FlametalTrail[1] : SteelTrail[1];
            float start = Time.time;
            bool first = true;
            while (arc)
            {
                float t = Time.time - start;
                if (t >= ArcSweep + ArcHold + ArcFade) break;
                float fade = t < ArcSweep + ArcHold ? 1f : 1f - (t - ArcSweep - ArcHold) / ArcFade;
                fade *= fade;
                float reveal = Mathf.Clamp01(t / ArcSweep);
                float heat = Mathf.Clamp01(t / (ArcSweep + ArcHold)); // white hot first, then its colour
                float grow = 1f + 0.06f * Mathf.Clamp01(t / (ArcSweep + ArcHold + ArcFade));
                for (int i = 0; i < n; i++)
                {
                    float u = i / (float)ArcSegments;                       // 0 at the left end, 1 at the right
                    float a = (u - 0.5f) * angle * Mathf.Deg2Rad;
                    float taper = Mathf.Sin(Mathf.PI * u);                   // thick in the middle, a point at both ends
                    float outer = range * grow;
                    if (flametal) outer += Random.Range(-0.05f, 0.05f) * range * taper; // a lightning edge
                    float inner = outer - range * ArcThick * taper;
                    var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    vertices[i * 2] = dir * outer;
                    vertices[i * 2 + 1] = dir * inner;
                    float shown = Mathf.Clamp01((reveal * 1.15f - u) / 0.15f); // swept in from the left
                    float flicker = flametal ? Random.Range(0.75f, 1f) : 1f;
                    var edge = Color.Lerp(Color.white, colour, heat);
                    var core = Color.Lerp(colour, deep, heat);
                    edge.a = fade * shown * Mathf.Sqrt(taper) * flicker;
                    core.a = 0f; // the inner edge fades out into the air
                    colours[i * 2] = edge;
                    colours[i * 2 + 1] = core;
                }
                mesh.vertices = vertices;
                mesh.colors = colours;
                if (first)
                {
                    mesh.triangles = triangles;
                    first = false;
                }
                mesh.RecalculateBounds();
                light.intensity = peakLight * fade * (flametal ? Random.Range(0.7f, 1f) : 1f);
                yield return null;
            }
            if (arc) Object.Destroy(arc);
            Object.Destroy(mesh);
        }

        // the thunderclap's cut as an afterimage along the whole dash: a long streak of blue white light at the cut's height
        // from where the dash began to a little past where it ended, turned to face the camera, faint and thin at the start
        // and widest and brightest at the player, bright along its middle line and fading to its edges, swept in from the
        // start, with a lightning flicker
        // it stays longer and draws brighter than the parry's arc so the whole dash can be seen
        private const float PathHeight = 0.8f, PathWidth = 2f, PathPast = 1f, PathFaint = 0.3f, PathHold = 0.6f, PathFade = 1.6f, PathLight = 7f;
        private static readonly Color PathTint = new Color(0.6f, 0.62f, 0.66f, 0.85f);
        private const int PathSegments = 40;
        private static Material _pathMaterial;

        internal static void SlashPath(Vector3 from, Vector3 to)
        {
            if (!Plugin.Instance) return;
            var material = PathMaterial();
            var along = to - from;
            along.y = 0f;
            if (!material || along.sqrMagnitude < 0.25f) return;
            float length = along.magnitude + PathPast;
            var path = new GameObject("IaidoKatana_thunderpath", typeof(MeshFilter), typeof(MeshRenderer));
            path.transform.position = from + Vector3.up * PathHeight;
            path.transform.rotation = Quaternion.LookRotation(along.normalized, Vector3.up);
            var mesh = new Mesh { name = "IaidoKatana_thunderpath" };
            path.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = path.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var light = new GameObject("IaidoKatana_thunderpath_light").AddComponent<Light>();
            light.transform.SetParent(path.transform, false);
            light.transform.localPosition = new Vector3(0f, 0f, length - PathPast); // at the player, where it is brightest
            light.type = LightType.Point;
            light.color = new Color(0.65f, 0.82f, 1f);
            light.range = Mathf.Max(6f, length * 0.6f);
            light.intensity = 0f;
            Plugin.Instance.StartCoroutine(PlayPath(path, mesh, light, length, along.normalized));
        }

        private static IEnumerator PlayPath(GameObject path, Mesh mesh, Light light, float length, Vector3 along)
        {
            int n = PathSegments + 1;
            var vertices = new Vector3[n * 3];
            var colours = new Color[n * 3];
            var triangles = new int[PathSegments * 24];
            for (int i = 0, k = 0; i < PathSegments; i++)
            {
                int o = i * 3, p = o + 3;
                // two strips (left edge to the middle, the middle to the right edge), both faces
                foreach (int s in new[] { 0, 1 })
                {
                    int a = o + s, b = o + s + 1, c = p + s, d = p + s + 1;
                    triangles[k++] = a; triangles[k++] = c; triangles[k++] = b;
                    triangles[k++] = b; triangles[k++] = c; triangles[k++] = d;
                    triangles[k++] = a; triangles[k++] = b; triangles[k++] = c;
                    triangles[k++] = b; triangles[k++] = d; triangles[k++] = c;
                }
            }
            var colour = FlametalTrail[0];
            var deep = FlametalTrail[1];
            float start = Time.time;
            bool first = true;
            while (path)
            {
                float t = Time.time - start;
                if (t >= ArcSweep + PathHold + PathFade) break;
                float fade = t < ArcSweep + PathHold ? 1f : 1f - (t - ArcSweep - PathHold) / PathFade;
                fade *= fade;
                float reveal = Mathf.Clamp01(t / ArcSweep);
                float heat = Mathf.Clamp01(t / (ArcSweep + PathHold));
                // the streak turns about its own line to face the camera, so it never shows edge on
                var camera = Camera.main;
                if (camera)
                {
                    var toCamera = camera.transform.position - (path.transform.position + along * length * 0.5f);
                    var face = toCamera - along * Vector3.Dot(toCamera, along);
                    if (face.sqrMagnitude > 1e-6f) path.transform.rotation = Quaternion.LookRotation(along, face.normalized);
                }
                float atPlayer = Mathf.Clamp01((length - PathPast) / length); // where the player stands along it
                for (int i = 0; i < n; i++)
                {
                    float u = i / (float)PathSegments;
                    // from a faint point at the start it grows to its widest and brightest at the player, then a short point
                    float rise = atPlayer > 0f ? Mathf.Clamp01(u / atPlayer) : 1f;
                    float taper = u <= atPlayer ? Mathf.Pow(rise, 0.8f) : Mathf.Sin(Mathf.PI * 0.5f * (1f - u) / Mathf.Max(1e-3f, 1f - atPlayer));
                    float strength = PathFaint + (1f - PathFaint) * rise;
                    float half = PathWidth * 0.5f * taper * (1f + Random.Range(-0.15f, 0.15f));
                    float z = u * length;
                    float wobble = Random.Range(-0.03f, 0.03f) * taper; // a lightning edge
                    vertices[i * 3] = new Vector3(-half, wobble, z);
                    vertices[i * 3 + 1] = new Vector3(0f, wobble, z);
                    vertices[i * 3 + 2] = new Vector3(half, wobble, z);
                    float shown = Mathf.Clamp01((reveal * 1.15f - u) / 0.15f);
                    var middle = Color.Lerp(Color.white, colour, heat);
                    middle.a = fade * shown * strength * Mathf.Sqrt(taper) * Random.Range(0.75f, 1f);
                    var edge = Color.Lerp(colour, deep, heat);
                    edge.a = 0f;
                    colours[i * 3] = edge;
                    colours[i * 3 + 1] = middle;
                    colours[i * 3 + 2] = edge;
                }
                mesh.vertices = vertices;
                mesh.colors = colours;
                if (first)
                {
                    mesh.triangles = triangles;
                    first = false;
                }
                mesh.RecalculateBounds();
                light.intensity = PathLight * fade * Random.Range(0.7f, 1f);
                yield return null;
            }
            if (path) Object.Destroy(path);
            Object.Destroy(mesh);
        }

        // the arc's plain additive material with a stronger tint, for the thunderclap's streak
        private static Material PathMaterial()
        {
            if (_pathMaterial) return _pathMaterial;
            var arc = ArcMaterial();
            if (!arc) return null;
            _pathMaterial = new Material(arc) { name = "IaidoKatana_thunderpath" };
            if (_pathMaterial.HasProperty("_TintColor")) _pathMaterial.SetColor("_TintColor", PathTint);
            return _pathMaterial;
        }

        // the trail's own additive material, plain, so the arc's vertex colours draw it
        private static Material ArcMaterial()
        {
            if (_arcMaterial) return _arcMaterial;
            var template = TrailMaterialOf("THSwordKrom");
            if (!template)
            {
                Plugin.Log.LogWarning("parry afterimage: the trail material was not found, no afterimage");
                return null;
            }
            _arcMaterial = new Material(template) { name = "IaidoKatana_afterimage", mainTexture = Texture2D.whiteTexture };
            if (_arcMaterial.HasProperty("_TintColor")) _arcMaterial.SetColor("_TintColor", new Color(0.6f, 0.58f, 0.55f, 0.5f));
            return _arcMaterial;
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
        // tiny flash of light and a small camera jolt
        private const float MarkHold = 0.12f, MarkFade = 0.6f, MarkGrow = 0.04f, MarkLength = 2.6f, MarkWidth = 0.07f;
        // the flash at the hit is a small glint that lights only about two metres around it, golden white for steel and
        // blue white for flametal, so hits do not light up dark places. the steel line itself is a little fainter
        private const float MarkLight = 1.2f, MarkRange = 2f, SteelMarkAlpha = 0.75f;
        private static readonly Color SteelMarkGlow = new Color(1f, 0.92f, 0.75f), FlametalMarkGlow = new Color(0.75f, 0.88f, 1f);
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
            light.color = flametal ? FlametalMarkGlow : SteelMarkGlow;
            light.range = MarkRange;
            light.intensity = 0f;
            var colour = flametal ? FlametalTrail[0] : SteelTrail[0];
            Plugin.Instance.StartCoroutine(PlayMark(mark, mesh, light, along.normalized, colour, MarkLight, flametal ? 1f : SteelMarkAlpha));
            var camera = GameCamera.instance;
            if (camera && Plugin.SlashShake.Value > 0f) camera.AddShake(camera.transform.position, 20f, Plugin.SlashShake.Value, false);
        }

        private static IEnumerator PlayMark(GameObject mark, Mesh mesh, Light light, Vector3 along, Color colour, float peakLight, float alpha)
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
                c.a = fade * alpha;
                for (int i = 0; i < 4; i++) colours[i] = c;
                mesh.colors = colours;
                if (light) light.intensity = peakLight * fade * (t < MarkHold ? 1f : 0.5f);
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
            _quad.colors = new[] { Color.white, Color.white, Color.white, Color.white }; // the trail shader multiplies by them
            _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _quad.RecalculateBounds();
            return _quad;
        }

        // the thunderclap's charge: a soft blue white ball of light under the given parent, facing the camera (its size and
        // turn set by ThunderCharge)
        private static Material _ballMaterial;

        internal static Transform ChargeGlow(Transform parent)
        {
            if (!_ballMaterial)
            {
                var template = TrailMaterialOf("THSwordKrom");
                if (!template)
                {
                    Plugin.Log.LogWarning("thunderclap charge: the trail material was not found, no glow");
                    return null;
                }
                _ballMaterial = new Material(template) { name = "IaidoKatana_chargeball", mainTexture = BallTexture() };
                if (_ballMaterial.HasProperty("_TintColor")) _ballMaterial.SetColor("_TintColor", new Color(0.5f, 0.75f, 1f, 1f));
            }
            var ball = new GameObject("IaidoKatana_chargeball", typeof(MeshFilter), typeof(MeshRenderer));
            ball.transform.SetParent(parent, false);
            ball.GetComponent<MeshFilter>().sharedMesh = Quad();
            var renderer = ball.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _ballMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ball.transform.localScale = Vector3.zero;
            return ball.transform;
        }

        // the slayer lightning's arcs crawling over the ball, their rate set by ThunderCharge
        internal static ParticleSystem ChargeArcs(Transform parent)
        {
            var ps = LightningCopy(parent, "IaidoKatana_chargearcs", Vector3.zero, "thunderclap charge");
            if (!ps) return null;
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // the arcs stay where they flash as the hands move
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            ps.Play();
            return ps;
        }

        private static Texture2D BallTexture()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "IaidoKatana_ball" };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float core = Mathf.Exp(-r * r * 14f) + 0.45f * Mathf.Exp(-r * r * 3f); // a bright core in a soft halo
                    byte a = (byte)(255f * Mathf.Clamp01(core * Mathf.Clamp01((1f - r) * 5f)));
                    pixels[y * size + x] = new Color32(a, a, a, a);
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
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
