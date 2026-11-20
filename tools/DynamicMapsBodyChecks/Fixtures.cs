using System.Reflection;
using Comfort.Common;
using EFT;

// Small foreign-contract fixtures. Production reflection and decisions are linked
// unchanged; the event counts distinguish rebinding from merely copying a field.
namespace EFT
{
    public enum EPlayerSide { Usec, Savage }
    public sealed class GameWorld { public LocalPlayer MainPlayer; }
    public sealed class Inventory { public object Equipment = new(); }
    public sealed class LocalPlayer
    {
        public GameWorld GameWorld;
        public EPlayerSide Side;
        public Events QuestController = new(), InventoryController = new();
        public Inventory Inventory = new();
    }
    public sealed class Events
    {
        public event Action Changed;
        public int Subscribers => Changed?.GetInvocationList().Length ?? 0;
        public void Raise() => Changed?.Invoke();
    }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace BepInEx.Bootstrap
{
    public static class Chainloader { public static readonly Dictionary<string, PluginInfo> PluginInfos = new(); }
    public sealed class PluginInfo { public object Instance; public Metadata Metadata = new(); }
    public sealed class Metadata { public Version Version = new(1, 3, 0); }
}
namespace HarmonyLib
{
    public static class AccessTools
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        public static FieldInfo Field(Type type, string name) => type.GetField(name, Flags);
        public static PropertyInfo Property(Type type, string name) => type.GetProperty(name, Flags);
    }
}
namespace Redeploy.Core
{
    public static class CoreLog
    {
        public static readonly List<string> Warnings = new();
        public static void LogDebug(string text) { }
        public static void LogWarning(string text) => Warnings.Add(text);
    }
}
namespace DynamicMaps.UI
{
    public sealed class MapView { }
    public sealed class ModdedMapScreen
    {
        private bool _providersActive = true, _providersInRaid = true;
        private MapView _mapView = new();
        private readonly Dictionary<Type, object> _dynamicMarkerProviders = new();
        public MapView View => _mapView;
        public void Visibility(bool active, bool raid) { _providersActive = active; _providersInRaid = raid; }
        public void SetMap(MapView map) => _mapView = map;
        public void Add(object provider) => _dynamicMarkerProviders[provider.GetType()] = provider;
        public void Remove<T>() => _dynamicMarkerProviders.Remove(typeof(T));
    }
}
namespace DynamicMaps
{
    public sealed class Plugin { public UI.ModdedMapScreen Map = new(); }
    public sealed class QuestMarkerProvider
    {
        private Events _questController;
        private UI.MapView _map;
        public int Hide, Show, Updates;
        public bool ThrowHide, ThrowShow;
        public readonly List<string> Calls = new();
        public object Controller => _questController;
        public void OnShowInRaid(UI.MapView map)
        {
            Show++; Calls.Add("show"); _map = map;
            if (ThrowShow) throw new InvalidOperationException("fixture show failed");
            var player = Singleton<GameWorld>.Instance.MainPlayer;
            if (player.Side == EPlayerSide.Savage) return;
            _questController = player.QuestController;
            _questController.Changed += Update;
        }
        public void OnHideInRaid(UI.MapView map)
        {
            Hide++; Calls.Add("hide");
            if (ThrowHide) throw new InvalidOperationException("fixture hide failed");
            if (_questController != null) _questController.Changed -= Update;
            _questController = null; _map = null;
        }
        private void Update() => Updates++;
        public void OnRaidEnd(UI.MapView map) => throw new Exception("must not clear world quest cache");
        public void OnDisable(UI.MapView map) => throw new Exception("must not disable provider");
    }
}
namespace DynamicMaps.DynamicMarkers
{
    public sealed class PlayerMarkerProvider
    {
        private readonly Marker _playerMarker = new();
        public LocalPlayer Bound => _playerMarker.Read();
        public void Seed(LocalPlayer player) => _playerMarker.Set(player);
    }
    public sealed class Marker
    {
        // Private in the actual 1.3.0 release.
        private LocalPlayer Player { get; set; }
        public LocalPlayer Read() => Player;
        public void Set(LocalPlayer player) => Player = player;
    }
    public sealed class LockedDoorMarkerMutator
    {
        private Events _inventoryController;
        private object _equipment;
        public int Hide, Show, Updates;
        public object Controller => _inventoryController;
        public object Equipment => _equipment;
        public void OnShowInRaid(UI.MapView map)
        {
            Show++;
            var player = Singleton<GameWorld>.Instance.MainPlayer;
            _inventoryController = player.InventoryController; _equipment = player.Inventory.Equipment;
            _inventoryController.Changed += Update;
        }
        public void OnHideInRaid(UI.MapView map)
        {
            Hide++;
            if (_inventoryController != null) _inventoryController.Changed -= Update;
            _inventoryController = null; _equipment = null;
        }
        private void Update() => Updates++;
        public void OnRaidEnd(UI.MapView map) => throw new Exception("must not end raid");
        public void OnDisable(UI.MapView map) => throw new Exception("must not disable provider");
    }
}
