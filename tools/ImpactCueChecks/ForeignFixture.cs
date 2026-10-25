// Deliberately small behavioral stand-in, not foreign implementation code.
// The real shipped DLL independently validates every member consumed by production.
using BepInEx;
using EFT;
using EFT.InventoryLogic;
using EFT.Vehicle;

namespace Dizzlow.ImpactCue;

[BepInPlugin("com.dizzlow.impactcue", "ImpactCue-UI-MOD", "2.0.3")]
public sealed class Plugin : BaseUnityPlugin { }
internal static class HudTelemetryRuntime
{
    internal static Player _raidPlayer;
    internal static GameWorld _raidWorld;
    internal static bool _raidActive;
    internal static float _nextBreathRefresh = 99f;
    internal static Weapon _cachedWeapon = new();
    internal static object _cachedMagazineSlot = new(), _cachedMagazineInventoryController = new(), _cachedAmmoSource = new(), _cachedHandsItem = new(),
        _cachedCurrentWeaponHudItem = new(), _cachedMovementContext = new(), _cachedPhysical = new(), _cachedStamina = new();
    internal static int Kills = 7, FeedEntries = 3, Resets, Restores, QuestRebinds, Refreshes, BtrCallbacks;
    internal static bool Enabled = true, PreferCustomHud = true;
    internal static bool ReadyOriginal(float now)
    {
        // Reviewed defect: a retained IsYourPlayer corpse is accepted unchanged.
        if (_raidActive && _raidPlayer != null && _raidPlayer.IsYourPlayer) return true;
        _raidWorld = Comfort.Common.Singleton<GameWorld>.Instance;
        _raidPlayer = GamePlayerOwner.MyPlayer;
        _raidActive = _raidPlayer != null;
        return _raidActive;
    }
    private static bool EnsureRaidReady(float now) => ReadyOriginal(now);
    internal static bool CanRenderTacticalHudNow => _raidActive;
    private static bool TryRestoreRaidContext(Player fallback, float now, string reason)
    {
        var world = Comfort.Common.Singleton<GameWorld>.Instance;
        var current = GamePlayerOwner.MyPlayer ?? fallback;
        if (world == null || current == null) return false;
        if (!_raidActive || !ReferenceEquals(world, _raidWorld)) { Resets++; Kills = FeedEntries = 0; }
        _raidWorld = world; _raidPlayer = current; _raidActive = true; Restores++;
        _cachedMovementContext = current.Movement; _cachedPhysical = current.Physical; _cachedStamina = current.Stamina;
        return true;
    }
    private static void ForceImmediateTelemetryRefresh() => Refreshes++;
    internal static void NotifyCompatibilityLocalPlayerHudStateChanged() { }
    internal static void NotifyCompatibilityQuestRuntimeChanged() { }
    private static bool TryAttachQuestEventHooks(Player player, GameWorld world, bool force) { QuestRebinds++; return true; }
    private static void DetachQuestEventHooks() { }
    private static void ClearWeaponSnapshot(bool clearHeldIdentity) { }
    internal static void NotifyLocalBtrPassengerAttached(Player player, BTRSide side, int place) { BtrCallbacks++; _raidPlayer = player; }
    internal static void NotifyLocalBtrPassengerDetached(Player player) { BtrCallbacks++; _raidPlayer = player; }
}
internal static class CombatIntrospection
{
    internal static Player _cachedLocalPlayer;
    internal static void CacheLocalPlayer(Player player) { if (player != null && player.IsYourPlayer) _cachedLocalPlayer = player; }
    internal static Player GetLocalPlayer() => _cachedLocalPlayer ?? GamePlayerOwner.MyPlayer;
    internal static bool IsLocalActor(object value)
    {
        if (value is Player player && player.IsYourPlayer) { _cachedLocalPlayer = player; return true; }
        return false;
    }
}
internal static class CrosshairTargetDetector { internal static void Reset() { } }
internal static class LowAmmoWarningManager { internal static void ResetObservation() { } }
