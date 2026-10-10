using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Iaido_Katana
{
    // the katana's own look, built at start from files inside the dll (art/make_katana.py makes them), so no unity
    // asset bundle is needed. the base sword's blade is hidden and the katana takes its place under "attach"
    internal static class KatanaModel
    {
        // the frame of krom's own mesh node: the sword runs along the mesh's y from the pommel, the hand at 0.33
        internal static readonly Vector3 NodePosition = new Vector3(0f, 0f, -0.33f);
        internal static readonly Quaternion NodeRotation = new Quaternion(0f, -0.7071068f, -0.7071068f, 0f);
        // where the swing trail ends, the katana's tip in the attach frame (the curve bends it away from the edge)
        private static readonly Vector3 TrailTip = new Vector3(-0.16f, 0f, 1.8f);

        // ImageConversion.LoadImage, looked up at run time because its module is built for a newer .NET than this mod
        private static readonly MethodInfo LoadImage = System.Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")
            ?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });

        // how bright the temper line glows
        private static readonly Color GlowStrength = new Color(2.4f, 2.4f, 2.4f);

        private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static readonly Dictionary<string, Vector3> Mouths = new Dictionary<string, Vector3>();
        private static readonly Dictionary<string, Mesh> Glows = new Dictionary<string, Mesh>();
        internal const float GlowFrom = 0.47f; // the glow shell starts past the guard, along the katana mesh
        private static Texture2D _flatNormal;

        // each katana's model and scabbard: its own when the dll has them (the flametal one), else the shared ones.
        // the scabbard is built along the same blade curve, so the blade lies inside it
        internal static Mesh KatanaMeshFor(string look) => LoadMesh(MeshName("katana", look));
        internal static Mesh SayaMeshFor(string look) => LoadMesh(MeshName("saya", look));

        private static string MeshName(string kind, string look) =>
            Assembly.GetExecutingAssembly().GetManifestResourceInfo($"Iaido_Katana.{kind}_{look}.obj") != null ? $"{kind}_{look}.obj" : $"{kind}.obj";

        // the middle of a scabbard's open end, in the mesh's frame. measured while the mesh loads, since the game can not
        // read a mesh back once it is on the graphics card
        internal static Vector3 SayaMouthFor(string look)
        {
            string name = MeshName("saya", look);
            if (!Mouths.ContainsKey(name)) LoadMesh(name);
            return Mouths.TryGetValue(name, out var mouth) ? mouth : Vector3.zero;
        }

        private static Vector3 OpenEnd(List<Vector3> vertices)
        {
            float low = float.MaxValue;
            foreach (var v in vertices) low = Mathf.Min(low, v.y);
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (var v in vertices)
                if (v.y < low + 0.01f)
                {
                    sum += v;
                    count++;
                }
            return count > 0 ? sum / count : Vector3.zero;
        }

        // the blade alone, for the lightning glow shell (the game's glow shader pushes it out along the normals itself)
        internal static Mesh GlowMeshFor(string look)
        {
            string name = MeshName("katana", look);
            if (!Glows.ContainsKey(name)) LoadMesh(name);
            return Glows.TryGetValue(name, out var glow) ? glow : null;
        }

        // the triangles past from along the mesh, with their own uvs running along the blade (u across, v along, in
        // about a tile per 40 cm, which the glow's lightning texture scrolls along)
        internal static void ShellOf(List<Vector3> v, List<Vector3> n, List<int> tris, float from,
            List<Vector3> sv, List<Vector3> sn, List<Vector2> suv, List<int> stris)
        {
            var map = new Dictionary<int, int>();
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                if (v[tris[i]].y < from || v[tris[i + 1]].y < from || v[tris[i + 2]].y < from) continue;
                for (int k = 0; k < 3; k++)
                {
                    int old = tris[i + k];
                    if (!map.TryGetValue(old, out int now))
                    {
                        now = sv.Count;
                        map[old] = now;
                        sv.Add(v[old]);
                        sn.Add(n[old]);
                        suv.Add(new Vector2(v[old].x * 4f, v[old].y * 1.2f));
                    }
                    stris.Add(now);
                }
            }
        }

        internal static Material MaterialFor(string variant) => Materials.TryGetValue(variant, out var m) ? m : null;

        // krom's material, the template for both katanas
        private static Material KromMaterial()
        {
            var krom = Jotunn.Managers.PrefabManager.Instance.GetPrefab("THSwordKrom");
            var attach = krom ? krom.transform.Find("attach") : null;
            var renderer = attach ? attach.GetComponentInChildren<MeshRenderer>(true) : null;
            return renderer ? renderer.sharedMaterial : null;
        }

        internal static bool Apply(GameObject prefab, string variant)
        {
            var attach = prefab.transform.Find("attach");
            var mesh = KatanaMeshFor(variant);
            var albedo = LoadTexture($"katana_{variant}_d.png");
            var metal = LoadTexture($"katana_{variant}_m.png");
            if (!attach || !mesh || !albedo || !metal)
            {
                Plugin.Log.LogWarning($"katana model for {prefab.name} could not be built, it keeps the placeholder look");
                return false;
            }

            // the base sword's blade goes out of sight, its effects (upgrade glow, trail) stay
            Material own = null;
            foreach (var filter in attach.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (!renderer) continue;
                if (!own) own = renderer.sharedMaterial;
                renderer.enabled = false;
                foreach (var collider in filter.GetComponents<Collider>()) collider.enabled = false;
            }
            // both katanas start from krom's material, the game's weapon shader. slayer's own material is unity's
            // standard shader with its glow switched off, which hid the flametal katana's look
            var template = KromMaterial() ?? own;
            if (!template) return false;

            var node = new GameObject("katana") { layer = attach.gameObject.layer };
            node.transform.SetParent(attach, false);
            node.transform.localPosition = NodePosition;
            node.transform.localRotation = NodeRotation;
            node.AddComponent<MeshFilter>().sharedMesh = mesh;
            var material = new Material(template) { name = "IaidoKatana_" + variant, color = Color.white };
            material.SetTexture("_MainTex", albedo);
            material.SetTexture("_MetallicGlossMap", metal);
            if (material.HasProperty("_BumpMap")) material.SetTexture("_BumpMap", FlatNormal());
            // the weapon shader tints metal with _MetalColor (black metal weapons use a green one). white: the textures
            // carry the colours, the flametal's heat tint too
            if (material.HasProperty("_MetalColor")) material.SetColor("_MetalColor", Color.white);
            var glow = LoadTexture($"katana_{variant}_e.png", optional: true);
            if (material.HasProperty("_EmissionMap"))
            {
                material.SetTexture("_EmissionMap", glow);
                material.SetColor("_EmissionColor", glow ? GlowStrength : Color.black);
            }
            node.AddComponent<MeshRenderer>().sharedMaterial = material;
            Materials[variant] = material;
            var box = node.AddComponent<BoxCollider>();
            box.center = mesh.bounds.center;
            box.size = mesh.bounds.size;

            // the game's swing trail on the greatsword swings, out to the katana's tip. the parry's cut and the
            // thunderclap draw their own (BladeTrail), the pose there is the mod's
            var tip = attach.Find("equiped/trail/tip");
            if (tip) tip.localPosition = TrailTip;
            // the flametal blade crackles with lightning while it is drawn, like the slayer's and splitnir's lightning
            if (variant == "flametal") KatanaFx.BladeLightning(node.transform, GlowMeshFor(variant));
            return true;
        }

        // an obj written by make_katana.py: one vertex per v/vt/vn, already in unity's frame and winding
        private static Mesh LoadMesh(string name)
        {
            if (Meshes.TryGetValue(name, out var cached) && cached) return cached;
            var bytes = ReadResource(name);
            if (bytes == null) return null;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var n = new List<Vector3>();
            var tris = new List<int>();
            foreach (var line in System.Text.Encoding.ASCII.GetString(bytes).Split('\n'))
            {
                var p = line.Trim().Split(' ');
                if (p.Length < 3) continue;
                switch (p[0])
                {
                    case "v": v.Add(new Vector3(F(p[1]), F(p[2]), F(p[3]))); break;
                    case "vt": uv.Add(new Vector2(F(p[1]), F(p[2]))); break;
                    case "vn": n.Add(new Vector3(F(p[1]), F(p[2]), F(p[3]))); break;
                    case "f": tris.Add(int.Parse(p[1]) - 1); tris.Add(int.Parse(p[2]) - 1); tris.Add(int.Parse(p[3]) - 1); break;
                }
            }
            if (v.Count == 0 || v.Count != uv.Count || v.Count != n.Count) return null;
            if (name.StartsWith("saya")) Mouths[name] = OpenEnd(v);
            if (name.StartsWith("katana"))
            {
                var sv = new List<Vector3>();
                var sn = new List<Vector3>();
                var suv = new List<Vector2>();
                var stris = new List<int>();
                ShellOf(v, n, tris, GlowFrom, sv, sn, suv, stris);
                if (stris.Count > 0)
                {
                    var glow = new Mesh { name = "IaidoKatana_glow_" + name };
                    glow.SetVertices(sv);
                    glow.SetNormals(sn);
                    glow.SetUVs(0, suv);
                    glow.SetTriangles(stris, 0);
                    glow.RecalculateBounds();
                    glow.UploadMeshData(true);
                    Glows[name] = glow;
                }
            }
            var mesh = new Mesh { name = "IaidoKatana_" + name };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            mesh.UploadMeshData(true);
            Meshes[name] = mesh;
            return mesh;
        }

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        // pixel art like the game's own weapon textures: point filtered, sRGB like krom's maps
        private static Texture2D LoadTexture(string name, bool optional = false)
        {
            var bytes = ReadResource(name, optional);
            if (bytes == null) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, false) { name = "IaidoKatana_" + name };
            if (LoadImage == null || !(bool)LoadImage.Invoke(null, new object[] { texture, bytes, true })) return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        // a normal map that bends nothing, read the same in both ways the game's shader may unpack it
        private static Texture2D FlatNormal()
        {
            if (_flatNormal) return _flatNormal;
            _flatNormal = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = "IaidoKatana_flat_normal" };
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(1f, 0.5f, 1f, 0.5f);
            _flatNormal.SetPixels(pixels);
            _flatNormal.Apply(false, true);
            return _flatNormal;
        }

        private static byte[] ReadResource(string name, bool optional = false)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Iaido_Katana." + name))
            {
                if (stream == null)
                {
                    if (!optional) Plugin.Log.LogWarning($"{name} is missing from the dll");
                    return null;
                }
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    return memory.ToArray();
                }
            }
        }
    }
}
