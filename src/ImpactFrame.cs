using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using UnityEngine.PostProcessing;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace Iaido_Katana
{
    // the counter's impact frame on the parrying player's own screen: a frozen black and white ink picture of the
    // cut with a streak through the enemy, a white flash out of it, a camera jolt and a sharp sound. nothing here is
    // sent to other players
    internal static class ImpactFrame
    {
        private static readonly AccessTools.FieldRef<CameraEffects, PostProcessingBehaviour> PostProcessing =
            AccessTools.FieldRefAccess<CameraEffects, PostProcessingBehaviour>("m_postProcessing");

        private const float ShakeRange = 10f;
        private const int InkWidth = 640;               // the ink picture is drawn this wide, pixel art like the game
        private const float CutAngle = 10f;             // the streak rises this many degrees to the right
        private const int InkSoftness = 1;              // blur radius before the split into two tones, in pixels
        private const float FocusRadius = 0.4f;         // share of the half screen diagonal around the hit drawn in full ink
        private const int SpeedLines = 96;              // slots for speed lines around the hit
        private static readonly Color32 Ink = new Color32(14, 12, 12, 255);
        private static readonly Color32 Paper = new Color32(242, 238, 228, 255);

        private static ColorGradingModel _grading;
        private static ColorGradingModel.Settings _saved;
        private static bool _savedEnabled;
        private static bool _flashing;
        private static int _flashId;
        private static bool _warnedFlash;
        private static readonly System.Collections.Generic.HashSet<string> WarnedSounds = new System.Collections.Generic.HashSet<string>();
        private const float CrashFade = 0.3f;           // seconds the cut crash takes to fade out

        private static RawImage _ink;
        private static Image _white;
        private static RenderTexture _grabTarget;
        private static Texture2D _grab;
        private static Texture2D _inkPicture;
        private static int _inkId;
        private static int _whiteId;

        // how long the ink picture will hide the screen after a hit, 0 when it is off (it starts a frame after the hit)
        internal static float InkHides => Plugin.ImpactEnabled.Value && Plugin.ImpactInk.Value && Plugin.ImpactInkSeconds.Value > 0f
            ? Plugin.ImpactInkSeconds.Value + 0.03f : 0f;

        // crash false leaves out the metal crash (the thunderclap has its own thunder)
        internal static void Play(Vector3 point, bool crash = true)
        {
            if (!Plugin.ImpactEnabled.Value) return;
            if (Plugin.ImpactInk.Value && Plugin.ImpactInkSeconds.Value > 0f) Plugin.Instance.StartCoroutine(InkFrame(++_inkId, point));
            else WhiteFlash();
            Flash();
            Shake();
            Sound(point, crash);
        }

        // one overlay over the whole screen, the ink picture under the white flash
        private static void EnsureOverlay()
        {
            if (_ink && _white) return;
            var root = new GameObject("IaidoKatana_impact_frame");
            Object.DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            _ink = FullScreen<RawImage>(root, "ink");
            _ink.enabled = false;
            _white = FullScreen<Image>(root, "flash");
            _white.color = new Color(1f, 1f, 1f, 0f);
        }

        private static T FullScreen<T>(GameObject root, string name) where T : Graphic
        {
            var graphic = new GameObject(name, typeof(RectTransform)).AddComponent<T>();
            graphic.transform.SetParent(root.transform, false);
            graphic.rectTransform.anchorMin = Vector2.zero;
            graphic.rectTransform.anchorMax = Vector2.one;
            graphic.rectTransform.offsetMin = Vector2.zero;
            graphic.rectTransform.offsetMax = Vector2.zero;
            graphic.raycastTarget = false;
            return graphic;
        }

        // the ink frame: one frame after the hit (so its sparks are in the picture) the camera's view is drawn small,
        // without the hud, turned into two-tone ink with a streak through the enemy, and held still for a moment
        private static IEnumerator InkFrame(int id, Vector3 point)
        {
            EnsureOverlay();
            yield return null;
            yield return new WaitForEndOfFrame();
            var camera = Camera.main;
            if (id != _inkId || !camera)
            {
                WhiteFlash();
                yield break;
            }

            int w = InkWidth;
            int h = Mathf.Max(1, Mathf.RoundToInt(w * (float)Screen.height / Mathf.Max(1, Screen.width)));
            if (!_grab || _grab.width != w || _grab.height != h)
            {
                if (_grabTarget) _grabTarget.Release();
                _grabTarget = new RenderTexture(w, h, 24) { name = "IaidoKatana_ink_grab" };
                _grab = new Texture2D(w, h, TextureFormat.RGB24, false) { name = "IaidoKatana_ink_read" };
                _inkPicture = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "IaidoKatana_ink", filterMode = FilterMode.Point };
            }

            var target = camera.targetTexture;
            var active = RenderTexture.active;
            try
            {
                camera.targetTexture = _grabTarget;
                camera.Render();
                RenderTexture.active = _grabTarget;
                _grab.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            }
            finally
            {
                camera.targetTexture = target;
                RenderTexture.active = active;
            }

            var pixels = _grab.GetPixels32();
            var screen = camera.WorldToScreenPoint(point);
            var cut = screen.z > 0f && Plugin.ImpactInkCut.Value
                ? new Vector2(screen.x / Mathf.Max(1, Screen.width) * w, screen.y / Mathf.Max(1, Screen.height) * h)
                : new Vector2(-1f, -1f);
            Inkify(pixels, w, h, cut, Plugin.ImpactInkInvert.Value);
            _inkPicture.SetPixels32(pixels);
            _inkPicture.Apply(false);
            _ink.texture = _inkPicture;
            _ink.enabled = true;

            float end = Time.unscaledTime + Plugin.ImpactInkSeconds.Value;
            while (Time.unscaledTime < end && id == _inkId) yield return null;
            if (id != _inkId) yield break;
            _ink.enabled = false;
            WhiteFlash(); // the white flash carries the eye from the ink picture back to the game
        }

        // two tones split at the picture's middle brightness, so about half of it is ink whatever the light. the cut
        // is a streak in the opposite tone, thickest at the enemy and running out to sharp ends
        internal static void Inkify(Color32[] pixels, int w, int h, Vector2 cut, bool invert)
        {
            var luma = new byte[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                luma[i] = (byte)((p.r * 77 + p.g * 150 + p.b * 29) >> 8);
            }
            // lightly smoothed, so grass turns into ink shapes instead of speckle but the fighters keep their outline
            Blur(luma, w, h, InkSoftness);

            // the picture centres on the hit (the screen middle when it is not in view). the split between ink and
            // paper is measured around it, so the fighters read clearly whatever the light
            var focus = cut.x >= 0f ? cut : new Vector2(w * 0.5f, h * 0.5f);
            float reach = (float)Math.Sqrt(w * w + h * h) * 0.5f;
            var histogram = new int[256];
            int counted = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (Distance(x, y, focus) / reach < FocusRadius)
                    {
                        histogram[luma[y * w + x]]++;
                        counted++;
                    }
            int threshold = 128;
            for (int k = 0, sum = 0; k < 256 && counted > 0; k++)
            {
                sum += histogram[k];
                if (sum >= counted / 2)
                {
                    threshold = k;
                    break;
                }
            }

            // towards the edges the picture fades to paper, and speed lines burst out of the hit through that paper
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float d = Distance(x, y, focus) / reach;
                    float fade = Smooth(FocusRadius, FocusRadius + 0.45f, d) * 255f;
                    bool paper = (luma[i] + fade > threshold) != invert;
                    if (paper && SpeedLine(x - focus.x, y - focus.y, d)) paper = false;
                    pixels[i] = paper ? Paper : Ink;
                }

            if (cut.x < 0f) return;
            float slope = (float)Math.Tan(CutAngle * Math.PI / 180.0);
            float halfLength = w * 0.45f;
            float thickest = Mathf.Max(2f, h * 0.014f);
            for (int x = Mathf.Max(0, (int)(cut.x - halfLength)); x < Mathf.Min(w, (int)(cut.x + halfLength)); x++)
            {
                float along = Mathf.Abs(x - cut.x) / halfLength;
                float half = thickest * (float)Math.Pow(1f - along, 0.7f);
                float centre = cut.y + (x - cut.x) * slope;
                for (int y = Mathf.Max(0, (int)(centre - half)); y <= Mathf.Min(h - 1, (int)(centre + half)); y++)
                {
                    int i = y * w + x;
                    pixels[i] = pixels[i].r > 128 ? Ink : Paper;
                }
            }
        }

        private static float Distance(int x, int y, Vector2 p) => (float)Math.Sqrt((x - p.x) * (x - p.x) + (y - p.y) * (y - p.y));

        private static float Smooth(float from, float to, float v)
        {
            float t = Mathf.Clamp01((v - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        // one of SpeedLines thin wedges around the hit, each with its own start, width and a small turn, from a fixed
        // hash so the same picture always gets the same lines. they widen a little as they run outward
        private static bool SpeedLine(float dx, float dy, float d)
        {
            if (d < FocusRadius * 0.8f) return false;
            double angle = Math.Atan2(dy, dx) + Math.PI;
            double step = 2.0 * Math.PI / SpeedLines;
            int k = (int)Math.Round(angle / step) % SpeedLines;
            uint hash = (uint)k * 2654435761u;
            float jitter = ((hash >> 8) & 255) / 255f - 0.5f;
            float start = FocusRadius * 0.8f + ((hash >> 16) & 255) / 255f * 0.35f;
            float width = 0.0025f + ((hash >> 24) & 255) / 255f * 0.006f;
            if (d < start || ((hash >> 4) & 3) == 0) return false; // a quarter of the slots stay empty, so the lines look drawn
            double centre = (k + jitter * 0.6) * step;
            double off = Math.Abs(angle - centre);
            if (off > Math.PI) off = 2.0 * Math.PI - off;
            return off < width * (0.6f + (d - start) * 2.5f);
        }

        // box blur, one pass across and one down, radius r pixels
        private static void Blur(byte[] values, int w, int h, int r)
        {
            if (r <= 0) return;
            var temp = new byte[values.Length];
            for (int y = 0; y < h; y++)
            {
                int row = y * w, sum = 0, count = 0;
                for (int x = -r; x < w; x++)
                {
                    if (x + r < w) { sum += values[row + x + r]; count++; }
                    if (x - r - 1 >= 0) { sum -= values[row + x - r - 1]; count--; }
                    if (x >= 0) temp[row + x] = (byte)(sum / count);
                }
            }
            for (int x = 0; x < w; x++)
            {
                int sum = 0, count = 0;
                for (int y = -r; y < h; y++)
                {
                    if (y + r < h) { sum += temp[(y + r) * w + x]; count++; }
                    if (y - r - 1 >= 0) { sum -= temp[(y - r - 1) * w + x]; count--; }
                    if (y >= 0) values[y * w + x] = (byte)(sum / count);
                }
            }
        }

        // a white sheet over the whole screen that fades out
        private static void WhiteFlash()
        {
            float alpha = Plugin.ImpactWhite.Value;
            float seconds = Plugin.ImpactFlashSeconds.Value;
            if (alpha <= 0f || seconds <= 0f) return;
            EnsureOverlay();
            Plugin.Instance.StartCoroutine(FadeWhite(++_whiteId, alpha, seconds));
        }

        // full white for the flash time, then gone over the same time again
        private static IEnumerator FadeWhite(int id, float alpha, float seconds)
        {
            float start = Time.unscaledTime;
            while (id == _whiteId && _white)
            {
                float t = Time.unscaledTime - start;
                float a = t < seconds ? alpha : alpha * Mathf.Clamp01(1f - (t - seconds) / seconds);
                _white.color = new Color(1f, 1f, 1f, a);
                if (a <= 0f) yield break;
                yield return null;
            }
        }

        // the game's color grading (saturation 1, contrast 1.2 in game) goes grey and harder for a moment
        private static void Flash()
        {
            float seconds = Plugin.ImpactFlashSeconds.Value;
            if (seconds <= 0f) return;
            var camera = CameraEffects.instance;
            var behaviour = camera ? PostProcessing(camera) : null;
            var grading = behaviour && behaviour.profile ? behaviour.profile.colorGrading : null;
            if (grading == null)
            {
                if (!_warnedFlash) Plugin.Log.LogWarning("no color grading on the camera, the impact frame has no flash");
                _warnedFlash = true;
                return;
            }

            // a flash during a flash keeps the first saved look, so the game's own one always comes back
            if (!_flashing || grading != _grading)
            {
                _grading = grading;
                _saved = grading.settings;
                _savedEnabled = grading.enabled;
            }
            var look = _saved;
            look.basic.saturation = 0f;
            look.basic.contrast = Mathf.Clamp(_saved.basic.contrast * Plugin.ImpactContrast.Value, 0f, 2f);
            grading.settings = look;
            grading.enabled = true;
            _flashing = true;
            // real time on the plugin's own object, so a freeze frame, a death or a logout cannot keep the screen grey
            Plugin.Instance.StartCoroutine(EndFlash(++_flashId, seconds));
        }

        private static IEnumerator EndFlash(int id, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (id == _flashId) Restore();
        }

        internal static void Restore()
        {
            if (!_flashing) return;
            _flashing = false;
            if (_grading == null) return;
            _grading.settings = _saved;
            _grading.enabled = _savedEnabled;
        }

        // the game's own camera shake, so the player's camera shake setting still applies
        private static void Shake()
        {
            float strength = Plugin.ImpactShake.Value;
            var camera = GameCamera.instance;
            if (strength <= 0f || !camera) return;
            camera.AddShake(camera.transform.position, ShakeRange, strength, false);
        }

        // a counter's impact plays the player's own parry sounds when there are any (Impact.OwnParrySounds), else the
        // mod's: a soft metal ring, a crash slowed down and cut short (off by default), and the extra layers
        // (Impact.ExtraSounds), all at Impact.ParrySoundVolume. the thunderclap's hit plays only the ring
        private static void Sound(Vector3 point, bool crash)
        {
            if (crash && OwnParrySounds.Play()) return;
            float volume = Plugin.ParrySoundVolume.Value;
            PlayLayer(point, Plugin.ImpactSound.Value, 0f, Plugin.ImpactVolume.Value * volume, 0f);
            if (!crash) return;
            PlayLayer(point, Plugin.ImpactCrash.Value, Plugin.ImpactCrashPitch.Value, Plugin.ImpactCrashVolume.Value * volume, Plugin.ImpactCrashSeconds.Value);
            var sets = ExtraLayers();
            if (sets.Length == 0) return;
            // one of the sets each time, never the same one twice in a row
            int set = 0;
            if (sets.Length > 1)
            {
                set = _lastSet < 0 ? Random.Range(0, sets.Length) : Random.Range(0, sets.Length - 1);
                if (_lastSet >= 0 && set >= _lastSet) set++;
            }
            _lastSet = set;
            var extra = sets[set];
            foreach (var layer in extra) PlayLayer(point, layer.Name, layer.PitchMax > layer.Pitch ? Random.Range(layer.Pitch, layer.PitchMax) : layer.Pitch, layer.Volume * volume, layer.Seconds, false, layer.Start);
            if (Plugin.LogCounters.Value && extra.Length > 0) Plugin.Log.LogInfo($"impact: sound set {set + 1} of {sets.Length}, {extra.Length} extra sounds played");
        }

        // the extra layers of the counter's impact sound by default, six sets of which each parry plays one (the user's
        // final set, 16 oct, parry_sound_listen13.py): the sword clash a little high, the pickaxe's ringing metal hit
        // raised, and slowed iron bars clanging from 0.3 s into their clip (F1 to F3), or the bars with a metal hammer
        // blow and the turret's armor clink (F4 to F6). F1 and F4 are the user's picks, the next two of each a little
        // brighter and a little deeper. the volumes are each layer's share of the clash's loudness
        internal const string DefaultExtraSounds =
            "sfx_sword_hit:0.975-1.235:1:0:0, sfx_pickaxe_hit_vibration_only:1.3:0.14:0:0, sfx_metalbars_break:0.65-0.78:0.796:2.2:0.3, sfx_metalbars_break:0.715-0.845:0.802:2.2:0.3 | " +
            "sfx_sword_hit:1.024-1.297:1:0:0, sfx_pickaxe_hit_vibration_only:1.404:0.145:0:0, sfx_metalbars_break:0.702-0.842:0.802:2.2:0.3, sfx_metalbars_break:0.772-0.913:0.808:2.2:0.3 | " +
            "sfx_sword_hit:0.926-1.173:1:0:0, sfx_pickaxe_hit_vibration_only:1.196:0.134:0:0, sfx_metalbars_break:0.598-0.718:0.867:2.2:0.3, sfx_metalbars_break:0.658-0.777:0.876:2.2:0.3 | " +
            "sfx_sword_hit:0.975-1.235:1:0:0, sfx_pickaxe_hit_vibration_only:1.3:0.14:0:0, sfx_metalbars_break:0.575-0.69:0.787:2.2:0.3, sfx_build_hammer_metal:0.575-0.69:0.581:2.2:0, fx_turret_newtarget:0.575-0.632:1.245:2.2:0 | " +
            "sfx_sword_hit:1.024-1.297:1:0:0, sfx_pickaxe_hit_vibration_only:1.404:0.145:0:0, sfx_metalbars_break:0.621-0.745:0.794:2.2:0.3, sfx_build_hammer_metal:0.621-0.745:0.597:2.2:0, fx_turret_newtarget:0.621-0.683:1.132:2.2:0 | " +
            "sfx_sword_hit:0.926-1.173:1:0:0, sfx_pickaxe_hit_vibration_only:1.196:0.134:0:0, sfx_metalbars_break:0.529-0.635:0.849:2.2:0.3, sfx_build_hammer_metal:0.529-0.635:0.456:2.2:0, fx_turret_newtarget:0.529-0.581:1.569:2.2:0";

        internal struct Layer
        {
            internal string Name;
            internal float Pitch, PitchMax, Volume, Seconds, Start;
        }

        private static string _extraText;
        private static Layer[][] _extra = new Layer[0][];
        private static int _lastSet = -1;

        // the sets of extra layers from Impact.ExtraSounds, parsed again only when the text changes
        private static Layer[][] ExtraLayers()
        {
            string text = Plugin.ImpactExtraSounds.Value ?? "";
            if (text == _extraText) return _extra;
            _extraText = text;
            _lastSet = -1;
            var warnings = new List<string>();
            _extra = ParseSets(text, warnings).ToArray();
            foreach (var warning in warnings) Plugin.Log.LogWarning("Impact.ExtraSounds: " + warning);
            return _extra;
        }

        // "set | set | ...", each set a list of layers. empty sets are left out
        internal static List<Layer[]> ParseSets(string text, List<string> warnings)
        {
            var sets = new List<Layer[]>();
            foreach (var part in (text ?? "").Split('|'))
            {
                var layers = ParseLayers(part, warnings);
                if (layers.Count > 0) sets.Add(layers.ToArray());
            }
            return sets;
        }

        // "name:pitch:volume:seconds[:start], ..." into layers. pitch 0 keeps the sound's own pitch range, a pitch like
        // 2.8-3 picks one in that range each time, seconds 0 plays it whole, start begins that many seconds into the clip.
        // broken entries are skipped with a warning
        internal static List<Layer> ParseLayers(string text, List<string> warnings)
        {
            var layers = new List<Layer>();
            foreach (var part in (text ?? "").Split(','))
            {
                var entry = part.Trim();
                if (entry.Length == 0) continue;
                var bits = entry.Split(':');
                float start = 0f;
                if ((bits.Length == 4 || bits.Length == 5 && Number(bits[4], 0f, 4f, out start)) && bits[0].Trim().Length > 0
                    && Pitch(bits[1], out float pitch, out float pitchMax) && Number(bits[2], 0f, 2f, out float volume) && Number(bits[3], 0f, 4f, out float seconds))
                    layers.Add(new Layer { Name = bits[0].Trim(), Pitch = pitch, PitchMax = pitchMax, Volume = volume, Seconds = seconds, Start = start });
                else
                    warnings.Add($"\"{entry}\" is not name:pitch:volume:seconds or name:pitch:volume:seconds:start (pitch 0 to 3 or a range like 2.8-3, volume 0 to 2, seconds and start 0 to 4), skipped");
            }
            return layers;
        }

        // one pitch, or a range "low-high" within 0.1 to 3
        private static bool Pitch(string text, out float low, out float high)
        {
            var range = text.Split('-');
            if (range.Length == 1)
            {
                bool ok = Number(range[0], 0f, 3f, out low);
                high = low;
                return ok;
            }
            low = high = 0f;
            return range.Length == 2 && Number(range[0], 0.1f, 3f, out low) && Number(range[1], 0.1f, 3f, out high) && high >= low;
        }

        private static bool Number(string text, float min, float max, out float value) =>
            float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

        // one clip of a game sound, played straight into the sound effects mixer. the sound prefabs are network
        // objects, so the prefab itself is not spawned. pitch 0 keeps the sound's own pitch range, below 1 it plays
        // slower and deeper. cut above 0 stops it after that many seconds, fading out over the end. an audio source
        // stops at volume 1, so a louder volume plays the same clip on more sources started on the same sample
        // the blade back in its scabbard: a short high metal click, heard around the player like any game sound
        internal static void Click(Vector3 point) =>
            PlayLayer(point, Plugin.ImpactClick.Value, Plugin.ImpactClickPitch.Value, Plugin.ImpactClickVolume.Value, ClickSeconds, true);

        private const float ClickSeconds = 0.12f;

        // start above 0 begins that many seconds into the clip, faded in over StartFade so it does not click
        internal static void PlayLayer(Vector3 point, string name, float pitch, float volume, float cut, bool spatial = false, float start = 0f)
        {
            if (string.IsNullOrEmpty(name) || volume <= 0f || !ZNetScene.instance) return;
            var prefab = ZNetScene.instance.GetPrefab(name);
            var sfx = prefab ? prefab.GetComponentInChildren<ZSFX>(true) : null;
            var template = prefab ? prefab.GetComponentInChildren<AudioSource>(true) : null;
            if (!sfx || !template || sfx.m_audioClips == null || sfx.m_audioClips.Length == 0)
            {
                if (WarnedSounds.Add(name)) Plugin.Log.LogWarning($"impact sound '{name}' not found or has no clips");
                return;
            }

            var clip = sfx.m_audioClips[Random.Range(0, sfx.m_audioClips.Length)];
            if (!clip) return;
            if (start >= clip.length - 0.05f) start = 0f;
            var holder = new GameObject("IaidoKatana_impact_sound");
            holder.transform.position = point;
            int count = Mathf.CeilToInt(volume);
            float each = volume / count;
            float speed = pitch > 0f ? pitch : sfx.m_maxPitch > 0f ? Random.Range(Mathf.Max(0.1f, sfx.m_minPitch), sfx.m_maxPitch) : 1f;
            double startAt = AudioSettings.dspTime + 0.02; // a shared start keeps the copies in phase, so they add up
            var sources = new AudioSource[count];
            for (int i = 0; i < count; i++)
            {
                var source = holder.AddComponent<AudioSource>();
                source.outputAudioMixerGroup = template.outputAudioMixerGroup;
                source.spatialBlend = 0f; // heard in full by the player who parried, wherever the camera is
                if (spatial)
                {
                    // heard from where it happens, fading with distance like the game's own sound
                    source.spatialBlend = template.spatialBlend;
                    source.rolloffMode = template.rolloffMode;
                    source.minDistance = template.minDistance;
                    source.maxDistance = template.maxDistance;
                    source.dopplerLevel = 0f;
                    if (template.rolloffMode == AudioRolloffMode.Custom)
                        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, template.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
                }
                source.pitch = speed;
                source.volume = start > 0f ? 0f : each;
                source.clip = clip;
                if (start > 0f) source.time = start;
                if (count > 1) source.PlayScheduled(startAt);
                else source.Play();
                sources[i] = source;
            }
            float length = (clip.length - start) / Mathf.Max(0.1f, speed);
            if (cut <= 0f || cut >= length) cut = 0f;
            else length = cut;
            if (cut > 0f || start > 0f) Plugin.Instance.StartCoroutine(FadeOut(sources, each, cut, start > 0f ? StartFade : 0f, count > 1 ? 0.02f : 0f));
            Object.Destroy(holder, length + 0.1f);
        }

        private const float StartFade = 0.03f;

        // a layer cut short fades out over its end (cut 0: not cut), one started inside its clip fades in first, from
        // fadeInAt (the scheduled start of copies played together)
        private static IEnumerator FadeOut(AudioSource[] sources, float volume, float cut, float fadeIn = 0f, float fadeInAt = 0f)
        {
            float fade = Mathf.Min(CrashFade, cut * 0.5f);
            float start = Time.unscaledTime;
            while (sources[0])
            {
                float t = Time.unscaledTime - start;
                bool done = cut > 0f && t >= cut;
                float k = fadeIn > 0f ? Mathf.Clamp01((t - fadeInAt) / fadeIn) : 1f;
                if (cut > 0f && t > cut - fade) k *= Mathf.Clamp01((cut - t) / fade);
                foreach (var source in sources)
                {
                    if (!source) continue;
                    if (done) source.Stop();
                    else source.volume = volume * k;
                }
                if (done || cut <= 0f && t >= fadeInAt + fadeIn) yield break;
                yield return null;
            }
        }
    }
}
