// Independent behavioral oracle for the reviewed source paths. No third-party code is bundled.
using System.Runtime.CompilerServices;
using BepInEx;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using UnityEngine;

namespace TarkovaCola.Client;

[BepInPlugin("com.tarkovacola.server", "Tarkova-Cola fixture", "4.0.0")]
public sealed class Plugin : BaseUnityPlugin
{
    public static Player Me => Singleton<GameWorld>.Instance?.MainPlayer;
}
internal static class Util
{
    [MethodImpl(MethodImplOptions.NoInlining)] internal static bool IsMe(Player body) => body != null && body.IsYourPlayer;
}
internal static class Tracker
{
    internal static bool _init = true, _deathDone, _leftBroken, _rightBroken;
    internal static object _hands;
    internal static Weapon _prevWeapon;
    internal static float _swapAt;
    internal static int ReadyCalls, Ticks, RaidProgress;
    [MethodImpl(MethodImplOptions.NoInlining)] internal static void Tick()
    {
        Ticks++;
        var body = Plugin.Me;
        if (body == null) return;
        if (!_init) { _init = true; ReadyCalls++; return; }
        QuickRevive.Tick(body); MuleKick.Tick(body); ElectricCherry.Tick(body); DoubleTap.Tick(body); Tombstone.Tick(body);
        if (!ReferenceEquals(_hands, body.HandsController)) { _hands = body.HandsController; RaidProgress++; }
    }
}
internal static class Perk
{
    internal static object MyBreath;
    internal static int ActivePerks = 6;
}
internal static class PerkFx
{
    internal static IAnimator _sped;
    internal static bool _hasPos, _wasBreath, _wasBreathD;
    internal static float _prevStamina, _next1Hz, Speed, _still, ConcussUntil, OwnWaveUntil, SwayMult;
    internal static void RefreshPhysical(Player body) { if (body.ThrowOnRefresh) throw new InvalidOperationException("fixture refresh failure"); body.PhysicalRefreshes++; }
}
internal static class StaminUp { internal static bool _hasPos; }
internal static class Juggernog { internal static bool _polled; }
internal static class QuickRevive
{
    internal static MedKitComponent _kit;
    internal static Item _pistol, _tempPistol, _primary;
    internal static DamageInfo _lethalInfo;
    internal static float _lastHit, _acc, _reserveUntil, _reserveReady, _next1Hz, _downedUntil, _nextForce, _removeTempAt;
    internal static bool Downed, Bypass, _regenOn, _noStimShown, _noKitShown, _reviveUsed;
    internal static int _pmcKills;
    internal static readonly HashSet<EBodyPart> _locked = new(), _wasDestroyed = new();
    internal static readonly HashSet<object> _bleeds = new();
    internal static readonly Dictionary<string, float> _botHit = new();
    internal static void Tick(Player body)
    {
        if (!Downed && _tempPistol != null && Time.time >= _removeTempAt) { _tempPistol.Slot.Removed++; _tempPistol = null; }
        if (!Downed) return;
        if (Time.time >= _downedUntil) { body.Dead = true; return; }
        body.ForcedItem = _pistol;
    }
}
internal static class DownedPronePatch
{
    [MethodImpl(MethodImplOptions.NoInlining)] internal static bool Prefix(MovementContext context, bool value)
        => value || !QuickRevive.Downed || !ReferenceEquals(Plugin.Me?.MovementContext, context);
}
internal static class MuleKick
{
    internal static Item _cur;
    internal static float _safetyAt, _swapAt, _muleDrawAt, _iconAt, _kickUntil, _meters;
    internal static bool _kickArmed, _safetyHeld, _hasPos, _kickUsed, _safetyDone;
    internal static int SlotIndex = 17;
    internal static readonly Dictionary<string, float> _apexAt = new();
    internal static void Tick(Player body)
    { if (!_safetyDone && _safetyHeld && Time.time >= _safetyAt + 0.3f) body.TriggerPresses++; }
}
internal static class ElectricCherry
{
    internal static bool _reloading, _wasBoosted, _sprintAtReload;
    internal static float _start, _startValue, _until, _lastReloadOp, _lastStamina, _emptiness;
    internal static readonly Dictionary<MovementContext, float> _bots = new();
    internal static void Tick(Player body) { if (_reloading) body.ElectricDischarges++; }
}
internal static class DoubleTap
{
    internal static int _hungryPending, _shotsSinceHit, _burst;
    internal static bool _doubleNow, _semi;
    internal static float _rhythm, _stepUntil, _lastHit, _lastShot, _maxSince, _irreg;
    internal static void Tick(Player body) { if (_hungryPending > 0) body.AmmoConsumed++; }
}
internal static class Tombstone
{
    internal static bool Specter, _hauntShot, _escapeHit, _offeringUsed;
    internal static float _until, _hiddenUntil, _decoyUntil, _nextLure, _escapeCheck, _hauntUntil, _endedAt, _readyAt;
    internal static int _hauntFrame;
    internal static object _tombGo;
    internal static readonly HashSet<string> _botUsed = new();
    internal static void DestroyTomb() { _tombGo = null; }
    internal static void Tick(Player body) { if (Specter) body.SpecterEffects++; }
}
internal static class PerkService
{
    internal static string _pendingOffering = "ordinary pending offering";
    internal static int Refunds = 13;
}
