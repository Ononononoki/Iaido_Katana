using System.Collections.Generic;
using UnityEngine;

namespace Iaido_Katana
{
    // the daisho's flying waves (Ranbu). each cut of the move sends one wave: a straight bar of light at the cut's angle,
    // as long as the parry's arc when it is born, that flies 10 m forward and widens sideways as it goes. a sheet of fading
    // streaks follows behind it, held half a second and then dissolved from its old end like the ammo trails of
    // AmmoVariants (AmmoTrailDissolve). on the owner's client it cuts every creature and chops every tree in its way once
    internal sealed class Crescent : MonoBehaviour
    {
        // all the numbers of the effect, in one place
        internal const float Range = 15f;        // meters a wave flies
        internal const float FlySeconds = 1.35f; // to cover it, fast at first and easing out
        internal const float HoldSeconds = 0.5f; // fully visible before the dissolve starts
        internal const float FadeSeconds = 0.5f; // the dissolve front takes this long from the old end to the new end
        internal const float Feather = 0.35f;    // the soft edge of that front, share of the path
        internal const float StartLength = 4f;   // meters from end to end when it is born (the parry's arc)
        internal const float Growth = 2.8f;      // its length at the end of the flight, times the start
        internal const float FinisherScale = 1.35f; // the two bars of the finisher's X
        internal const float Height = 1.1f;      // the bar's middle above the feet
        internal const float BarWidth = 0.16f;   // meters across the light of the bar
        internal const float WakeLength = 4.5f;  // meters of fading streaks behind the bar
        internal const float MinWidth = 3f, ReachUp = 1.4f, Depth = 0.7f; // the hit volume's least width, half its height (from the ground to above a head) and depth, meters
        internal const int Points = 13;          // along the bar, every other one a brighter streak of the wake
        internal const int MaxAlive = 24;
        // a wave takes the colour of its weapon's own effect (the game's red lightning, its cyan frostfire) from the
        // start, with no white heat, and is fainter than a cut's trail (the owner, 15 Nov: the streaks were too bright)
        private static readonly Color[] RedWave = { new Color(1f, 0.45f, 0.3f, 1f), new Color(1f, 0.17f, 0f, 0.85f) };
        private static readonly Color[] FrostWave = { new Color(0.45f, 0.9f, 1f, 1f), new Color(0.09f, 0.66f, 0.86f, 0.85f) };
        internal const float Brightness = 0.6f;

        private static readonly List<Crescent> Alive = new List<Crescent>();

        internal sealed class Group
        {
            internal readonly HashSet<Character> Hit = new HashSet<Character>(); // one hit per creature per cut
            internal readonly HashSet<IDestructible> Chopped = new HashSet<IDestructible>(); // one chop per tree per cut
            internal Player Owner;
            internal bool Wakizashi;
            internal float DamageShare;
            internal bool Local;
        }

        private Vector3 _origin;      // where it was born (the player's feet)
        private Quaternion _rotation; // its frame: x right, y up, z the way it flies
        private Vector3 _forward;
        private Vector3 _along;       // the bar's line in that frame
        private float _angle;         // of that line from level, radians
        private float _scale;
        private Group _group;
        private int _look;
        private ParticleSystem _sparks;
        private float _born;
        private Mesh _mesh;
        private Vector3[] _vertices;
        private Color[] _colours;
        private bool _built;

        // the angle of a cut's straight line from level, radians: from where the blade tip's path began to where it ended,
        // seen from behind the player. a cut straight at the camera gets a diagonal
        internal static float AngleOf(Vector3[] path)
        {
            if (path == null || path.Length < 2) return Mathf.PI * 0.25f;
            var d = path[path.Length - 1] - path[0];
            return d.x * d.x + d.y * d.y < 0.04f ? Mathf.PI * 0.25f : Mathf.Atan2(d.y, d.x);
        }

        // one cut's wave. path is the blade tip's path in the player's frame (feet origin), root the player's turn, origin
        // the feet
        internal static void Spawn(Vector3[] path, Vector3 origin, Quaternion root, int look, Group group, float scale)
        {
            if (path == null || path.Length < 2 || !Plugin.AfterimageOn.Value && !group.Local) return;
            var material = KatanaFx.ArcMaterial();
            if (!material) return;
            Alive.RemoveAll(c => !c);
            // the oldest go first (Destroy only takes effect at the end of the frame, so they leave the list here)
            while (Alive.Count >= MaxAlive)
            {
                var oldest = Alive[0];
                Alive.RemoveAt(0);
                if (oldest) Destroy(oldest.gameObject);
            }
            var go = new GameObject("IaidoKatana_wave", typeof(MeshFilter), typeof(MeshRenderer));
            var wave = go.AddComponent<Crescent>();
            wave.Begin(AngleOf(path), origin, root, look, group, material, scale);
            Alive.Add(wave);
        }

        private void Begin(float angle, Vector3 origin, Quaternion rotation, int look, Group group, Material material, float scale)
        {
            _origin = origin;
            _rotation = rotation;
            _forward = rotation * Vector3.forward;
            _angle = angle;
            _along = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            _scale = scale;
            _look = look;
            _group = group;
            _born = Time.time;
            transform.position = origin;
            transform.rotation = rotation;
            _mesh = new Mesh { name = "IaidoKatana_wave" };
            _mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _sparks = KatanaFx.WaveSparks(transform, look);
            _vertices = new Vector3[Points * 5];
            _colours = new Color[Points * 5];
            var bar = Triangles(Points);
            var wake = WakeTriangles(Points);
            var triangles = new int[bar.Length + wake.Length];
            bar.CopyTo(triangles, 0);
            wake.CopyTo(triangles, bar.Length);
            _mesh.vertices = _vertices;
            _mesh.colors = _colours;
            _mesh.triangles = triangles;
            _built = true;
            Draw(0f);
        }

        // the bar's triangles: three vertices across each point, two strips between two points, both faces
        internal static int[] Triangles(int points)
        {
            var triangles = new int[Mathf.Max(0, points - 1) * 24];
            int k = 0;
            for (int i = 0; i < points - 1; i++)
                for (int row = 0; row < 2; row++)
                {
                    int a = i * 3 + row, b = a + 3;
                    triangles[k++] = a; triangles[k++] = b; triangles[k++] = a + 1;
                    triangles[k++] = a + 1; triangles[k++] = b; triangles[k++] = b + 1;
                    triangles[k++] = a; triangles[k++] = a + 1; triangles[k++] = b;
                    triangles[k++] = a + 1; triangles[k++] = b + 1; triangles[k++] = b;
                }
            return triangles;
        }

        // the wake's triangles: after the bar's vertices every point has a head (at the bar) and a tail (behind it), one
        // strip between two points, both faces
        internal static int[] WakeTriangles(int points)
        {
            var triangles = new int[Mathf.Max(0, points - 1) * 12];
            int k = 0;
            for (int i = 0; i < points - 1; i++)
            {
                int head = points * 3 + i * 2, tail = head + 1, nextHead = head + 2, nextTail = head + 3;
                triangles[k++] = head; triangles[k++] = tail; triangles[k++] = nextHead;
                triangles[k++] = nextHead; triangles[k++] = tail; triangles[k++] = nextTail;
                triangles[k++] = head; triangles[k++] = nextHead; triangles[k++] = tail;
                triangles[k++] = nextHead; triangles[k++] = nextTail; triangles[k++] = tail;
            }
            return triangles;
        }

        // meters out from where it was born, a quick start easing out
        internal static float DistanceAt(float age)
        {
            float s = Mathf.Clamp01(age / FlySeconds);
            return Range * (1f - (1f - s) * (1f - s));
        }

        // how much longer the bar is this far out: it widens evenly with the distance, so its ends fly straight lines
        internal static float GrowthOver(float distance) => 1f + (Growth - 1f) * Mathf.Clamp01(distance / Range);

        internal static float GrowthAt(float age) => GrowthOver(DistanceAt(age));

        // how much is left at u (0 the old end, 1 the new end): the dissolve front starts after the hold and passes from
        // the old end to the new one with a soft edge (AmmoTrailDissolve.Dissolve)
        internal static float AlphaAt(float age, float u)
        {
            if (age < 0f) return 0f;
            float front = Mathf.Clamp01((age - HoldSeconds) / FadeSeconds) * (1f + Feather);
            float p = Mathf.Clamp01((front - u) / Feather);
            return 1f - p * p * (3f - 2f * p);
        }

        // the hit volume at an age: its middle in the world, its half sizes, and the turn it stands in. it stands on the
        // aim line and reaches from the ground to above a head whatever the bar's angle, as wide as the bar reaches to the
        // sides (a steep bar still takes MinWidth), so a creature in the wave's way is not missed
        internal static void VolumeAt(Vector3 origin, Quaternion rotation, float angle, float scale, float age, out Vector3 middle, out Vector3 half, out Quaternion frame)
        {
            float g = GrowthAt(age);
            var way = rotation * Vector3.forward;
            float flat = Mathf.Sqrt(way.x * way.x + way.z * way.z);
            float yaw = Mathf.Atan2(way.x, way.z) * 0.5f;
            frame = new Quaternion(0f, Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            middle = origin + frame * new Vector3(0f, ReachUp, DistanceAt(age) * flat);
            float reach = StartLength * 0.5f * scale * g * Mathf.Abs(Mathf.Cos(angle));
            half = new Vector3(Mathf.Max(reach, MinWidth * 0.5f * g), ReachUp, Depth * 0.5f);
        }

        private void LateUpdate()
        {
            if (!_built) return;
            float age = Time.time - _born;
            if (age >= HoldSeconds + FadeSeconds + 0.05f)
            {
                Destroy(gameObject);
                return;
            }
            if (_group != null && _group.Local) CutThrough(age);
            Draw(age);
            Sparkle(age);
        }

        // the sparkle rides the bar while it is whole, and stops when the dissolve begins (what it left stays its time)
        private void Sparkle(float age)
        {
            if (!_sparks) return;
            if (age >= HoldSeconds)
            {
                if (_sparks.isEmitting) _sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                return;
            }
            float distance = DistanceAt(age), half = StartLength * 0.5f * _scale * GrowthOver(distance);
            var along = transform.rotation * _along;
            KatanaFx.WaveSparksAlong(_sparks, transform.TransformPoint(new Vector3(0f, Height, distance)) - along * half, along, half * 2f);
        }

        private void OnDestroy()
        {
            Alive.Remove(this);
            if (_mesh) Destroy(_mesh);
        }

        // the bar (three vertices across each point, soft edges, turned to the camera) and the wake behind it, in the
        // wave's own frame
        private void Draw(float age)
        {
            var colours = _look == 3 ? FrostWave : RedWave;
            var camera = Camera.main;
            var eye = camera ? transform.InverseTransformPoint(camera.transform.position) : new Vector3(0f, Height, -5f);
            float distance = DistanceAt(age), behind = Mathf.Max(0f, distance - WakeLength);
            float half = StartLength * 0.5f * _scale * GrowthOver(distance), halfBehind = StartLength * 0.5f * _scale * GrowthOver(behind);
            float thick = BarWidth * 0.5f;
            var core = Color.Lerp(colours[0], colours[1], Mathf.Clamp01(age / (HoldSeconds + FadeSeconds)));
            float barAlpha = AlphaAt(age, 1f) * Brightness, wakeAlpha = AlphaAt(age, 0.55f) * Brightness;
            for (int i = 0; i < Points; i++)
            {
                float u = i / (float)(Points - 1), along = u * 2f - 1f;
                float taper = Mathf.Sin(Mathf.PI * Mathf.Clamp01(0.06f + 0.88f * u)); // thin ends, full in the middle
                var point = new Vector3(0f, Height, distance) + _along * (along * half);
                var side = Vector3.Cross(_along, eye - point);
                side = side.sqrMagnitude < 1e-6f ? Vector3.up : side.normalized;
                float width = thick * (0.3f + 0.7f * taper);
                var edge = colours[1];
                edge.a = 0f;
                var middle = core;
                middle.a = barAlpha;
                _vertices[i * 3] = point - side * width;
                _vertices[i * 3 + 1] = point;
                _vertices[i * 3 + 2] = point + side * width;
                _colours[i * 3] = edge;
                _colours[i * 3 + 1] = middle;
                _colours[i * 3 + 2] = edge;
                // the wake: a bright streak from every other point, a faint sheet between them
                var head = colours[1];
                head.a = wakeAlpha * taper * (i % 2 == 0 ? 0.55f : 0.14f);
                var tail = colours[1];
                tail.a = 0f;
                int k = Points * 3 + i * 2;
                _vertices[k] = point;
                _vertices[k + 1] = new Vector3(0f, Height, behind) + _along * (along * halfBehind);
                _colours[k] = head;
                _colours[k + 1] = tail;
            }
            _mesh.vertices = _vertices;
            _mesh.colors = _colours;
            _mesh.RecalculateBounds();
        }

        // the owner's client: every creature and tree the volume passes since last frame, once per cut. the volume is
        // checked at steps no longer than a third of its depth along that way, so a fast wave skips nothing
        private void CutThrough(float age)
        {
            var owner = _group.Owner;
            if (age > FlySeconds || !owner || owner.IsDead()) return;
            VolumeAt(_origin, _rotation, _angle, _scale, age, out var middle, out var half, out var frame);
            VolumeAt(_origin, _rotation, _angle, _scale, Mathf.Max(0f, age - Time.deltaTime), out var before, out _, out _);
            var weapon = owner.GetCurrentWeapon();
            bool chops = Chop.On(weapon) && Katana.IsThunderblood(weapon);
            int steps = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(before, middle) / (Depth / 3f)), 1, 24);
            for (int i = 1; i <= steps; i++)
            {
                var at = Vector3.Lerp(before, middle, i / (float)steps);
                foreach (var collider in Physics.OverlapBox(at, half, frame, Daisho.Creatures))
                {
                    var who = collider ? collider.GetComponentInParent<Character>() : null;
                    if (!who || who == owner || who.IsDead() || _group.Hit.Contains(who) || !Thunderclap.Enemy(owner, who)) continue;
                    if (Physics.Linecast(owner.GetCenterPoint(), who.GetCenterPoint(), Thunderclap.Walls)) continue;
                    _group.Hit.Add(who);
                    Daisho.CrescentHit(_group, who, _forward);
                }
                if (!chops) continue;
                foreach (var collider in Physics.OverlapBox(at, half, frame, Chop.Mask))
                {
                    var tree = Chop.Tree(collider);
                    if (tree == null || !_group.Chopped.Add(tree)) continue;
                    Daisho.WaveChop(_group, weapon, tree, collider.ClosestPointOnBounds(at), _forward);
                }
            }
        }
    }
}
