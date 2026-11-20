using EFT;
using UnityEngine;

namespace AdvancedMovement
{
    [BepInEx.BepInPlugin("com.sage.advancedmovement", "Advanced Movement", "1.0.1")]
    public class Plugin : BepInEx.BaseUnityPlugin { }
}
namespace AdvancedMovement.States
{
    internal abstract class ProfileMoveState : BaseMovementState
    {
        public float SavedSpeedLimit = 5;
        public override void Exit(bool same) => MovementContext.SpeedLimit = SavedSpeedLimit;
    }
    internal class SlideState : ProfileMoveState { }
    internal class DiveState : BaseMovementState
    {
        public float SavedSpeedLimit = 5;
        public override void Exit(bool same) => MovementContext.SpeedLimit = SavedSpeedLimit;
    }
}
namespace AdvancedMovement.Moves
{
    internal static class MoveController
    {
        internal static Player _player; internal static States.ProfileMoveState _move; internal static States.DiveState _dive;
        public static bool HoldStill; internal static float _settleUntil; internal static bool _settleLogged;
        public static bool Busy => _move != null || _dive != null;
    }
    internal static class SteadyWeapon { internal static float _lastMoveTime; }
    internal static class AirSlide
    {
        internal static Player _player; internal static bool _queued, _wasInAir;
        internal static Vector3 _airVelocity; internal static float _queuedAt;
    }
    internal static class SlideJump
    {
        internal static MovementContext _context; internal static float _armedAt; internal static Vector3 _velocity;
    }
}
namespace AdvancedMovement.Viewmodel
{
    internal static class GesturePlayer
    {
        internal static Player _player; internal static bool Active; internal static int Restores, Stops;
        internal static bool ThrowOnRestore;
        public static void RestoreWeaponRoot() { Restores++; if (ThrowOnRestore) throw new Exception("injected gesture failure"); }
        public static void Stop() { _player = null; Active = false; Stops++; }
    }
    internal static class WeaponPin
    {
        internal static Player.FirearmController _firearm; internal static bool Pinned; internal static int Restores, Clears;
        public static void Restore() => Restores++;
        public static void Clear() { Pinned = false; _firearm = null; Clears++; }
    }
}
namespace AdvancedMovement.Body
{
    internal sealed class BodyAnimator
    {
        public Player Player { get; }
        internal bool Active = true; internal int Stops;
        public BodyAnimator(Player player) { Player = player; }
        public void Stop() { Active = false; Stops++; }
    }
    internal static class BodyPlayer
    {
        internal static BodyAnimator _local;
        internal static readonly Dictionary<Player, BodyAnimator> Remote = new();
    }
}
namespace AdvancedMovement.Audio
{
    internal static class SlideAudio
    {
        internal static Player _player; internal static GameObject _go;
        internal static AudioSource _loop, _shots; internal static bool _active, _easing;
    }
}
