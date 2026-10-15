using System.Collections.Generic;
using UnityEngine;

namespace Iaido_Katana
{
    // slow windup and a fast strike for the local player's katana swings. pace 1 keeps the vanilla time to the hit,
    // a higher pace gets there that much sooner. a swing with a total length (the heavy attack) gets its raise
    // stretched or squeezed so the whole swing, recovery included, lasts that long. at the hit the animation goes
    // back to the game's own speed, so the recovery is untouched. unknown swings learn their timing once
    internal static class SwingWarp
    {
        // animation seconds from the start of the swing to its hit, measured in game on 10 Oct 2026
        private static readonly Dictionary<string, float> HitTimes = new Dictionary<string, float>
        {
            ["greatsword0"] = 0.62f,
            ["greatsword1"] = 0.50f,
            ["greatsword2"] = 0.64f,
            ["battleaxe_attack"] = 1.40f,
            ["swing_sledge"] = 1.33f,
            ["shovel0"] = 0.301f, // the upper body swings (their clips' hit events)
            ["shovel1"] = 0.314f,
            ["shovel2"] = 0.340f,
            ["greatsword_secondary"] = 1.33f // the katana's heavy with the blade out, its clip on the sledge's clock (hit event 1.296)
        };

        // real seconds to the hit the katana's upper body swings keep: the greatsword swings' of 0.1.1
        private static readonly float[] EvenHitSeconds = { 0.62f, 0.50f, 0.64f };

        // real seconds from the hit until the swing ends (recovery at normal speed), measured and kept up to date
        private static readonly Dictionary<string, float> AfterHitTimes = new Dictionary<string, float>
        {
            ["swing_sledge"] = 0.66f,
            ["shovel2"] = 0.66f,
            ["greatsword_secondary"] = 0.65f // our 1.85 s clip leaves 0.55 s after the hit, plus the blend out (estimated)
        };

        private const float EntryDelay = 0.03f; // the animator needs a frame or two to enter the swing

        private static string _key;
        private static float _pace;
        private static float _strikeBoost;
        private static float _total;
        private static float _animTime;
        private static bool _active;
        private static bool _hitDone;
        private static bool _seenAttack;
        private static float _startTime;
        private static float _hitTime;
        private static float _gameSpeed;
        private static Animator _animator;
        private static bool _cleared;
        private static bool _even;

        internal static bool Active => _active && !_hitDone;

        // the animation time where the heavy's slow raise ends and its fast strike begins (both heavy clips reach the
        // hit at the same time)
        internal static float HeavyRaiseEnd => HitTimes["greatsword_secondary"] * Plugin.WindupShare.Value;

        // pace speeds up the whole way to the hit, strikeBoost only the strike (the windup slows down to match).
        // total above 0 sets the length of the whole swing instead, by changing only the raise
        internal static void Begin(string key, float pace, float strikeBoost, float total)
        {
            _key = key;
            _pace = pace;
            _strikeBoost = strikeBoost;
            _total = total;
            _animTime = 0f;
            _hitDone = false;
            _seenAttack = false;
            _startTime = Time.time;
            _gameSpeed = 1f; // a swing starts at normal speed, whatever an earlier swing or the counter left behind
            _animator = null;
            _cleared = false;
            _even = false;
            _active = true;
        }

        // an upper body swing (Swing.UpperBody): the legs walk on the same animator, so its speed stays even up to the hit
        // (a slow windup and fast strike would make the walk stutter, the katana's own cut keys give that shape). a normal
        // swing reaches its hit as late as the greatsword's did, the heavy fills total seconds
        internal static void BeginEven(string key, int chain, float total)
        {
            float hit = HitTimes.TryGetValue(key, out float h) ? h : 0.3f;
            float pace = total > 0f ? 1f : hit / EvenHitSeconds[Mathf.Clamp(chain, 0, EvenHitSeconds.Length - 1)];
            Begin(key, pace, 1f, total);
            _even = true;
        }

        internal static void OnHit()
        {
            if (!_active || _hitDone) return;
            _hitDone = true;
            _hitTime = Time.time;
            // hand the animation back at the speed the game wanted, otherwise the recovery would play fast too
            if (_animator) _animator.speed = _gameSpeed;
            if (HitTimes.ContainsKey(_key)) return;
            HitTimes[_key] = _animTime;
            if (Plugin.LogCounters.Value) Plugin.Log.LogInfo($"learned swing timing {_key}: hit after {_animTime:0.00} s of animation");
        }

        // the game's Speed animation events are held back until the hit, while this swing sets the speed
        internal static bool OverridesSpeedEvents => Active && _animator;

        internal static void RecordGameSpeed(float speed) => _gameSpeed = speed;

        // runs every fixed step for the local player. paused means a freeze frame is holding the animation
        internal static void Step(Player player, Animator animator, float dt, bool paused)
        {
            if (!_active) return;
            // the game reads "in attack" from the animator, which needs a frame or two to enter the swing
            if (!player.InAttack())
            {
                if (!_seenAttack && Time.time - _startTime <= 0.5f) return;
                End();
                return;
            }
            _seenAttack = true;
            if (paused) return;

            if (!_hitDone)
            {
                float speed = SpeedAt(_animTime);
                if (speed > 0f)
                {
                    _animator = animator;
                    animator.speed = speed;
                }
                else if (!_cleared) animator.speed = 1f; // once, clears a speed left over from the counter swing
            }
            _cleared = true;
            _animTime += dt * animator.speed;
        }

        private static void End()
        {
            _active = false;
            float total = Time.time - _startTime;
            if (_hitDone && _seenAttack) AfterHitTimes[_key] = Time.time - _hitTime;
            if (Plugin.LogCounters.Value)
                Plugin.Log.LogInfo(_hitDone
                    ? $"swing {_key} ended after {total:0.00} s ({_hitTime - _startTime:0.00} s to the hit, {Time.time - _hitTime:0.00} s after it)"
                    : $"swing {_key} ended after {total:0.00} s, {(_seenAttack ? "" : "never entered the attack animation, ")}no hit moment");
        }

        // the speed before the hit, or -1 when this swing plays at the game's speed
        private static float SpeedAt(float animTime)
        {
            bool known = HitTimes.TryGetValue(_key, out float hitTime);
            if (_even)
            {
                if (!known || !Plugin.SwingTimeWarp.Value) return -1f;
                if (_total > 0f && AfterHitTimes.TryGetValue(_key, out float after))
                    return hitTime / Mathf.Max(0.2f, _total - after - EntryDelay);
                return _pace;
            }
            float share = Plugin.WindupShare.Value;
            float windup = Plugin.WindupSpeed.Value;
            float strike = Katana.StrikeSpeed(share, windup);
            if (_strikeBoost > 1f && strike > 0f)
            {
                float boosted = strike * _strikeBoost;
                float slower = Katana.WindupFor(share, boosted);
                if (slower > 0f)
                {
                    strike = boosted;
                    windup = slower;
                }
            }
            if (!known || !Plugin.SwingTimeWarp.Value || strike <= 0f) return _pace != 1f ? _pace : -1f;

            // a set total length: keep the slash speed, give the rest of the time to the raise
            if (_total > 0f && AfterHitTimes.TryGetValue(_key, out float afterHit))
            {
                float slash = strike * _pace;
                float raise = Katana.RaiseSpeedForTotal(hitTime, share, slash, EntryDelay, afterHit, _total);
                if (raise > 0f) return Katana.WarpSpeed(animTime, hitTime, share, raise, slash, 1f);
            }
            return Katana.WarpSpeed(animTime, hitTime, share, windup, strike, _pace);
        }
    }
}
