using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Iaido_Katana
{
    // the thunderclap's gathering, heard and seen on every client with the mod: an electric hum rising in pitch,
    // lightning crackles coming faster, the dverger mages' charge ending on the cut, the fallen valkyrie's scream just
    // before it, a ready zing and a thunder crack on the cut (the game's own clips, mixed live after the user's pick of
    // 15 oct, devtools\session_2026-10-15b), and a ball of lightning at the hands that grows and bursts on the cut.
    // timed from the gathering's start with its length (Thunderclap.GatherSeconds), so any gather time keeps the cut on
    // the crack
    internal sealed class ThunderCharge : MonoBehaviour
    {
        // one clip of the mix: the game prefab that holds it, its name, how many copies of it play together and how loud
        // each copy is (at its peak for the hum and the crackles, which swell), where its sound starts and how long it
        // sounds (measured by thunder_mix_numbers.py). more copies, louder: the copies start on the same sample and add up
        private sealed class Part
        {
            internal string Prefab, Clip;
            internal int Copies;
            internal float Each, Head, Active;
        }

        private static readonly Part Hum = new Part { Prefab = "fx_Lightning", Clip = "EnergyRifle_ElecHumLoop1", Copies = 1, Each = 0.695f, Active = 2.31f };
        private static readonly Part Crackle = new Part { Prefab = "ChainLightning", Clip = "Player_Weapons_Enchant_Lightning_Chain_0", Copies = 1, Each = 0.2f, Active = 0.5f };
        private static readonly Part Rise = new Part { Prefab = "sfx_dverger_heal_start", Clip = "Enemy_Dverger_ChargeUp_Big_01", Copies = 1, Each = 1f, Head = 0.2f, Active = 2.538f };
        private static readonly Part Scream = new Part { Prefab = "sfx_fallenvalkyrie_attack_spin_charge", Clip = "Enemy_FallenValkyrie_Attack_Wingspin_Charge_01", Copies = 1, Each = 0.595f, Active = 1.514f };
        private static readonly Part Ready = new Part { Prefab = "sfx_weapons_lightning_enable", Clip = "Player_Weapons_Enchant_Lightning_Enable_02", Copies = 1, Each = 0.595f, Active = 1.5f };
        private static readonly Part Crack = new Part { Prefab = "vfx_lever", Clip = "Thunder01", Copies = 1, Each = 1f, Active = 3.48f };

        private const float HumFrom = 0.7f, HumTo = 1.35f, HumFadeIn = 0.3f, HumEnd = 0.05f;
        private const float CrackleStart = 0.15f, CrackleSlow = 0.42f, CrackleFast = 0.08f, CrackleFade = 0.15f;
        private const float RiseMaxPitch = 1.5f, RiseFadeIn = 0.15f, RiseEarly = 0.05f;
        private const float ScreamBefore = 0.6f, ReadyBefore = 0.25f, ReadyPitch = 1.15f, CrackBefore = 0.45f;
        private const float CancelFade = 0.15f;
        private const double StartDelay = 0.03; // the copies of one sound start together on the audio clock this soon
        private const float Falloff = 40f; // meters until the gathering is not heard

        private const float BallFrom = 0.06f, BallTo = 0.36f, BurstSeconds = 0.18f, BurstGrow = 2.6f, GlowTowardCamera = 0.35f;
        private static readonly Color BallLight = new Color(0.6f, 0.8f, 1f);

        private Player _player;
        private Transform _right, _left;
        private float _start, _prep;
        private bool _cancelled, _rise, _scream, _ready, _crack;
        private float _cancelAt = -1f, _nextCrackle;
        private int _crackles;
        private AudioSource _hum;
        private readonly List<(AudioSource Source, float StopAt, float Volume)> _sources = new List<(AudioSource, float, float)>();
        private Transform _glow;
        private ParticleSystem _arcs;
        private Light _light;
        private float _burstAt = -1f;

        private static readonly Dictionary<string, (AudioClip Clip, AudioMixerGroup Mixer)> Clips = new Dictionary<string, (AudioClip, AudioMixerGroup)>();
        private static readonly HashSet<string> Warned = new HashSet<string>();

        private float Elapsed => Time.time - _start;
        private static float Master => Plugin.ChargeSoundVolume.Value;

        internal static ThunderCharge Begin(Player player, Transform rightHand, Transform leftHand, float prep)
        {
            if (!player) return null;
            var go = new GameObject("IaidoKatana_thundercharge");
            var charge = go.AddComponent<ThunderCharge>();
            charge._player = player;
            charge._right = rightHand;
            charge._left = leftHand;
            charge._start = Time.time;
            charge._prep = prep;
            charge._nextCrackle = CrackleStart;
            go.transform.position = charge.Hands();
            if (Plugin.ChargeBallOn.Value) charge.MakeBall();
            if (Master > 0f) charge._hum = charge.Play(Hum, Hum.Clip, 0f, HumFrom, 0f, true);
            if (Plugin.LogCounters.Value) Plugin.Log.LogInfo($"thunderclap gathering sound and ball for {player.GetPlayerName()}, {prep:0.00} s");
            return charge;
        }

        // called off before the cut: everything fades out fast and the crack never comes. after the cut it plays on
        internal void Cancel()
        {
            if (_cancelled || Elapsed >= _prep) return;
            _cancelled = true;
            _cancelAt = Time.time;
        }

        // the middle of the two hands on the grip, or in front of the hips when the bones are not known
        private Vector3 Hands()
        {
            if (_right && _left) return (_right.position + _left.position) * 0.5f;
            if (_right) return _right.position;
            return _player ? _player.transform.position + Vector3.up * 0.9f + _player.transform.forward * 0.3f : transform.position;
        }

        private void Update()
        {
            if (!_player)
            {
                Destroy(gameObject);
                return;
            }
            transform.position = Hands();
            float t = Elapsed;
            if (_cancelled)
            {
                float k = 1f - (Time.time - _cancelAt) / CancelFade;
                foreach (var (source, _, volume) in _sources) if (source) source.volume = volume * Mathf.Clamp01(k);
                if (_glow) _glow.localScale *= Mathf.Clamp01(k);
                if (_light) _light.intensity *= Mathf.Clamp01(k);
                if (k <= 0f) Destroy(gameObject);
                return;
            }
            UpdateSound(t);
            UpdateBall(t);
            if (t > _prep + Crack.Active + 0.5f) Destroy(gameObject);
        }

        private void UpdateSound(float t)
        {
            float p = Mathf.Clamp01(t / _prep);
            if (_hum)
            {
                float fadeIn = Mathf.Clamp01(t / HumFadeIn), end = Mathf.Clamp01((_prep - t) / HumEnd);
                float each = Mathf.Min(1f, Hum.Each * Master * (0.4f + 0.6f * p) * fadeIn * end);
                foreach (var (source, _, _) in _sources)
                {
                    if (!source || source.clip != _hum.clip) continue;
                    source.pitch = Mathf.Lerp(HumFrom, HumTo, p);
                    source.volume = each;
                    if (t >= _prep) source.Stop();
                }
            }
            // crackles closer and closer, higher and louder, until just before the cut
            while (_nextCrackle <= t && _nextCrackle < _prep - CrackleStart && Master > 0f)
            {
                float q = _nextCrackle / _prep;
                var source = Play(Crackle, Crackle.Clip + (1 + _crackles % 6), 0f, 0.95f + 0.4f * q, Crackle.Each * (0.25f + 0.75f * q));
                if (source) _sources[_sources.Count - 1] = (source, Time.time + Crackle.Active / source.pitch, source.volume);
                _crackles++;
                _nextCrackle += Mathf.Lerp(CrackleSlow, CrackleFast, q);
            }
            // the rise ends on the cut: sped up (to a limit) when it is longer than the gathering, else it starts later
            if (!_rise && Master > 0f)
            {
                var (startAt, pitch, skip) = RiseTiming(_prep);
                if (t >= startAt)
                {
                    _rise = true;
                    Play(Rise, Rise.Clip, Rise.Head + skip, pitch, Rise.Each);
                }
            }
            if (!_scream && t >= _prep - ScreamBefore && Master > 0f)
            {
                _scream = true;
                Play(Scream, Scream.Clip, Mathf.Max(0f, ScreamBefore - _prep), 1f, Scream.Each);
            }
            if (!_ready && t >= _prep - ReadyBefore && Master > 0f)
            {
                _ready = true;
                Play(Ready, Ready.Clip, Mathf.Max(0f, ReadyBefore - _prep) * ReadyPitch, ReadyPitch, Ready.Each);
            }
            if (!_crack && t >= _prep - CrackBefore && Master > 0f)
            {
                _crack = true;
                Play(Crack, Crack.Clip, Mathf.Max(0f, CrackBefore - _prep), 1f, Crack.Each);
            }
            // fade the crackles' tails
            for (int i = 0; i < _sources.Count; i++)
            {
                var (source, stopAt, volume) = _sources[i];
                if (!source || stopAt <= 0f) continue;
                float left = stopAt - Time.time;
                if (left <= 0f) source.Stop();
                else if (left < CrackleFade) source.volume = volume * left / CrackleFade;
            }
        }

        // when the rise starts, how fast it plays and how much of its start is skipped, so its end lands on the cut
        internal static (float StartAt, float Pitch, float Skip) RiseTiming(float prep)
        {
            float pitch = Mathf.Clamp(Rise.Active / Mathf.Max(0.01f, prep - RiseEarly), 1f, RiseMaxPitch);
            float sounding = Rise.Active / pitch;
            return (Mathf.Max(0f, prep - sounding), pitch, sounding > prep ? (sounding - prep) * pitch : 0f);
        }

        internal static float RiseActive => Rise.Active;

        // a game clip played here, following the hands, heard around the player like the game's own effects
        private AudioSource Play(Part part, string clipName, float from, float pitch, float each, bool loop = false)
        {
            var (clip, mixer) = ClipOf(part.Prefab, clipName);
            if (!clip) return null;
            AudioSource first = null;
            // the part's copies, every one from the same sample on the same audio clock tick, so they add up instead of
            // echoing
            int count = Mathf.Max(1, part.Copies);
            double at = AudioSettings.dspTime + StartDelay;
            int fromSample = Mathf.Clamp((int)(from * clip.frequency), 0, Mathf.Max(0, clip.samples - 1));
            for (int i = 0; i < count; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.clip = clip;
                source.outputAudioMixerGroup = mixer;
                source.loop = loop;
                source.pitch = pitch;
                source.volume = Mathf.Min(1f, each * Master);
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 3f;
                source.maxDistance = Falloff;
                source.dopplerLevel = 0f;
                source.timeSamples = fromSample;
                source.PlayScheduled(at);
                _sources.Add((source, -1f, source.volume));
                if (!first) first = source;
            }
            return first;
        }

        // the clip by name from a game prefab's sound effects, looked up once
        internal static (AudioClip Clip, AudioMixerGroup Mixer) ClipOf(string prefabName, string clipName)
        {
            string key = prefabName + "/" + clipName;
            if (Clips.TryGetValue(key, out var found)) return found;
            var prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(prefabName) : null;
            AudioClip clip = null;
            AudioMixerGroup mixer = null;
            if (prefab)
            {
                foreach (var sfx in prefab.GetComponentsInChildren<ZSFX>(true))
                {
                    if (sfx.m_audioClips == null) continue;
                    foreach (var c in sfx.m_audioClips)
                        if (c && c.name == clipName)
                        {
                            clip = c;
                            var source = sfx.GetComponent<AudioSource>();
                            mixer = source ? source.outputAudioMixerGroup : null;
                        }
                }
                if (!clip)
                    foreach (var source in prefab.GetComponentsInChildren<AudioSource>(true))
                        if (source.clip && source.clip.name == clipName)
                        {
                            clip = source.clip;
                            mixer = source.outputAudioMixerGroup;
                        }
            }
            // some sound prefabs are not in the game's prefab list (the lightning enchant's), but the lightning weapons that
            // use them keep their clips loaded: found by name, played through a game sound's mixer
            if (!clip)
            {
                foreach (var c in Resources.FindObjectsOfTypeAll<AudioClip>())
                    if (c && c.name == clipName)
                    {
                        clip = c;
                        break;
                    }
                if (clip && !mixer) mixer = ClipOf(Rise.Prefab, Rise.Clip).Mixer;
            }
            if (!clip && Warned.Add(key)) Plugin.Log.LogWarning($"thunderclap gathering sound: clip '{clipName}' not found in '{prefabName}' or loaded, that part stays silent");
            Clips[key] = (clip, mixer);
            return (clip, mixer);
        }

        private void MakeBall()
        {
            _glow = KatanaFx.ChargeGlow(transform);
            _arcs = KatanaFx.ChargeArcs(transform);
            _light = gameObject.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = BallLight;
            _light.range = 3f;
            _light.intensity = 0f;
        }

        // the ball grows with the charge and pulses faster, then bursts on the cut
        private void UpdateBall(float t)
        {
            if (!_glow && !_arcs && !_light) return;
            if (t < _prep)
            {
                float p = Mathf.Clamp01(t / _prep);
                float pulse = 1f + 0.12f * Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(2f, 12f, p) * t);
                float size = Mathf.Lerp(BallFrom, BallTo, p * Mathf.Sqrt(p)) * pulse;
                if (_glow)
                {
                    _glow.localScale = Vector3.one * size * 2f;
                    var camera = Camera.main;
                    if (camera)
                    {
                        _glow.rotation = camera.transform.rotation;
                        // a little toward the camera, so the body in front of the hands does not hide it
                        var toCamera = camera.transform.position - transform.position;
                        _glow.position = transform.position + (toCamera.sqrMagnitude > 1e-4f ? toCamera.normalized * GlowTowardCamera : Vector3.zero);
                    }
                }
                if (_arcs)
                {
                    var shape = _arcs.shape;
                    shape.radius = size * 0.8f;
                    var emission = _arcs.emission;
                    emission.rateOverTime = Mathf.Lerp(6f, 90f, p);
                }
                if (_light)
                {
                    _light.range = Mathf.Lerp(2f, 6f, p);
                    _light.intensity = Mathf.Lerp(0.3f, 4f, p) * Random.Range(0.8f, 1f);
                }
                return;
            }
            if (_burstAt < 0f)
            {
                _burstAt = Time.time;
                if (_arcs)
                {
                    var emission = _arcs.emission;
                    emission.rateOverTime = 0f;
                    _arcs.Emit(40);
                }
            }
            float b = (Time.time - _burstAt) / BurstSeconds;
            if (b >= 1f)
            {
                if (_glow) Destroy(_glow.gameObject);
                if (_light) Destroy(_light);
                _glow = null;
                _light = null;
                return;
            }
            if (_glow) _glow.localScale = Vector3.one * BallTo * 2f * Mathf.Lerp(1f, BurstGrow, b) * (1f - b);
            if (_light) _light.intensity = 10f * (1f - b);
        }
    }
}
