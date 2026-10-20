using System;
using HarmonyLib;
using UnityEngine;

namespace Iaido_Katana
{
    internal static class AnimationSafety
    {
        internal const float EntryGrace = 0.5f;
        internal const float StallSeconds = 4f;
        internal const float SpeedTolerance = 0.0001f;
        internal const float ProgressTolerance = 0.00001f;

        internal static readonly AccessTools.FieldRef<Humanoid, Attack> CurrentAttack = AccessTools.FieldRefAccess<Humanoid, Attack>("m_currentAttack");
        internal static readonly AccessTools.FieldRef<Character, Animator> AnimatorOf = AccessTools.FieldRefAccess<Character, Animator>("m_animator");
        internal static readonly AccessTools.FieldRef<Character, CharacterAnimEvent> AnimEvent = AccessTools.FieldRefAccess<Character, CharacterAnimEvent>("m_animEvent");
        internal static readonly AccessTools.FieldRef<CharacterAnimEvent, float> PauseTimer = AccessTools.FieldRefAccess<CharacterAnimEvent, float>("m_pauseTimer");
        internal static readonly AccessTools.FieldRef<CharacterAnimEvent, float> PauseSpeed = AccessTools.FieldRefAccess<CharacterAnimEvent, float>("m_pauseSpeed");

        internal static bool SameOwner(object player, object animator, object weapon, object attack,
            object currentPlayer, object currentAnimator, object currentWeapon, object currentAttack, bool requireAttack) =>
            player != null && animator != null && weapon != null
            && ReferenceEquals(player, currentPlayer) && ReferenceEquals(animator, currentAnimator)
            && ReferenceEquals(weapon, currentWeapon) && (!requireAttack || attack != null && ReferenceEquals(attack, currentAttack));

        internal static int AttackLayer(Animator animator)
        {
            for (int i = 0; i < animator.layerCount; i++)
            {
                if (i > 0 && animator.GetLayerWeight(i) <= 0f) continue;
                if (animator.GetCurrentAnimatorStateInfo(i).IsTag("attack")
                    || animator.IsInTransition(i) && animator.GetNextAnimatorStateInfo(i).IsTag("attack")) return i;
            }
            return 0;
        }

        internal static bool Attacking(Animator animator)
        {
            if (!animator || !animator.isInitialized) return false;
            for (int i = 0; i < animator.layerCount; i++)
            {
                if (i > 0 && animator.GetLayerWeight(i) <= 0f) continue;
                if (animator.GetCurrentAnimatorStateInfo(i).IsTag("attack")
                    || animator.IsInTransition(i) && animator.GetNextAnimatorStateInfo(i).IsTag("attack")) return true;
            }
            return false;
        }

        // only restore a value this mod still owns, including a speed saved by hit stop
        internal sealed class SpeedState
        {
            internal float GameSpeed;
            internal float LastSpeed;
            internal bool Written;

            internal void Write(float speed) { LastSpeed = speed; Written = true; }

            internal bool Restore(float current, out float speed)
            {
                speed = GameSpeed;
                bool restore = Written && Math.Abs(current - LastSpeed) <= SpeedTolerance;
                Written = false;
                return restore;
            }
        }

        // a paused frame or any state/time change restarts the no-progress interval
        internal sealed class Progress
        {
            private bool _seen;
            private int _state, _next;
            private float _time, _nextTime, _held;

            internal void Reset() { _seen = false; _held = 0f; }

            internal bool Step(int state, float time, int next, float nextTime, bool paused, float dt)
            {
                bool changed = !_seen || state != _state || next != _next
                    || Math.Abs(time - _time) > ProgressTolerance || Math.Abs(nextTime - _nextTime) > ProgressTolerance;
                _seen = true;
                _state = state;
                _next = next;
                _time = time;
                _nextTime = nextTime;
                _held = changed || paused ? 0f : _held + Math.Max(0f, dt);
                return _held >= StallSeconds;
            }
        }
    }

    // one speed writer's player, animator, weapon and optional real attack
    internal sealed class AnimationLease
    {
        internal Player Player;
        internal Animator Animator;
        private ItemDrop.ItemData _weapon;
        private Attack _attack;
        private bool _requireAttack;
        private readonly AnimationSafety.SpeedState _speed = new AnimationSafety.SpeedState();
        private readonly AnimationSafety.Progress _progress = new AnimationSafety.Progress();

        internal void Begin(Player player, Attack attack, bool requireAttack)
        {
            Clear();
            Player = player;
            Animator = player ? AnimationSafety.AnimatorOf(player) : null;
            _weapon = player ? player.GetCurrentWeapon() : null;
            _attack = attack;
            _requireAttack = requireAttack;
            if (!Animator) return;
            var events = AnimationSafety.AnimEvent(player);
            _speed.GameSpeed = events && AnimationSafety.PauseTimer(events) > 0f
                ? AnimationSafety.PauseSpeed(events) : Animator.speed;
        }

        internal bool Matches(Player player, Animator animator) => player && animator && animator.isInitialized && player == global::Player.m_localPlayer
            && !player.IsDead() && !player.InDodge() && !player.IsStaggering()
            && AnimationSafety.SameOwner(Player, Animator, _weapon, _attack, player, animator,
                player.GetCurrentWeapon(), AnimationSafety.CurrentAttack(player), _requireAttack);

        internal bool Current => Player && Animator && Matches(Player, Animator);

        internal void GameSpeed(float speed) => _speed.GameSpeed = speed;

        internal void Write(float speed)
        {
            _speed.Write(speed);
            Animator.speed = speed;
        }

        internal bool Stalled(float dt, bool paused)
        {
            if (!_speed.Written || !Animator) { _progress.Reset(); return false; }
            int layer = AnimationSafety.AttackLayer(Animator);
            var current = Animator.GetCurrentAnimatorStateInfo(layer);
            var next = Animator.IsInTransition(layer) ? Animator.GetNextAnimatorStateInfo(layer) : default;
            return _progress.Step(current.fullPathHash, current.normalizedTime, next.fullPathHash, next.normalizedTime, paused, dt);
        }

        internal void Restore()
        {
            if (!Animator) { _speed.Written = false; return; }
            var events = Player ? AnimationSafety.AnimEvent(Player) : null;
            if (events && AnimationSafety.PauseTimer(events) > 0f)
            {
                if (_speed.Restore(AnimationSafety.PauseSpeed(events), out float speed)) AnimationSafety.PauseSpeed(events) = speed;
            }
            else if (_speed.Restore(Animator.speed, out float speed)) Animator.speed = speed;
        }

        internal void Clear()
        {
            Restore();
            Player = null;
            Animator = null;
            _weapon = null;
            _attack = null;
            _progress.Reset();
        }
    }
}
