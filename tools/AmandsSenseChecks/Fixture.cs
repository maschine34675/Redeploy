using System.Runtime.CompilerServices;
using BepInEx;
using EFT;

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)]
    public class BepInPlugin : Attribute
    {
        public BepInPlugin(string guid, string name, string version) { GUID = guid; Version = new(version); }
        public string GUID { get; }
        public Version Version { get; }
    }
    public class BaseUnityPlugin { }
    public class PluginInfo { public BaseUnityPlugin Instance; public BepInPlugin Metadata; }
}
namespace BepInEx.Bootstrap { public static class Chainloader { public static Dictionary<string, PluginInfo> PluginInfos = new(); } }
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; public static bool Instantiated => Instance != null; } }
namespace Redeploy.Core { public static class CoreLog { public static void LogWarning(string message) => Console.WriteLine(message); } }
namespace EFT
{
    public class Profile { }
    public enum GameStatus { Started, Starting, Running, Stopped }
    public class AbstractGame { }
    public class LocalGame : AbstractGame { public GameStatus Status = GameStatus.Running; public Profile Profile; public LocalPlayer LocalPlayer; public GameWorld GameWorld; }
    public class GameWorld { public Player MainPlayer; }
    public class HideoutGameWorld : GameWorld { }
    public class Player
    {
        public class PlayerInventoryController { }
        public PlayerInventoryController InventoryController = new();
        public bool IsYourPlayer, Destroyed;
        public Profile Profile;
        public GameWorld GameWorld;
        [MethodImpl(MethodImplOptions.NoInlining)] public void Init() { if (IsYourPlayer) GameWorld.MainPlayer = this; }
        public static bool operator ==(Player a, Player b)
        {
            bool left = ReferenceEquals(a, null) || a.Destroyed, right = ReferenceEquals(b, null) || b.Destroyed;
            return left || right ? left && right : ReferenceEquals(a, b);
        }
        public static bool operator !=(Player a, Player b) => !(a == b);
        public override bool Equals(object other) => ReferenceEquals(other, this);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }
    public class LocalPlayer : Player { }
}
namespace AmandsSense
{
    [BepInPlugin("xyz.drakia.Sense", "Sense fixture", "3.1.0")]
    public class AmandsSensePlugin : BaseUnityPlugin { }
}
namespace AmandsSense.Models { public struct SenseDeadPlayer { public Player victim; } }
namespace AmandsSense.Components
{
    using Models;
    public class AmandsSenseClass
    {
        public static Player Player;
        public static Player.PlayerInventoryController inventoryControllerClass;
        public static List<SenseDeadPlayer> DeadPlayers = new();
        public static Dictionary<int, Action> Active = new();
        public static int Reloads;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Clear()
        {
            foreach (var marker in Active.Values) marker();
            Active.Clear();
            DeadPlayers.Clear();
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ReloadFiles(bool onlySounds) => Reloads++;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void SenseDeadBodies()
        {
            foreach (var record in DeadPlayers)
                if (record.victim == null) throw new InvalidOperationException("destroyed Sense victim");
        }
    }
}
namespace AmandsSense.Patches
{
    using Components;
    public static class AmandsPlayerPatch
    {
        public static void PatchPostFix(ref Player __instance)
        {
            if (!__instance.IsYourPlayer) return;
            AmandsSenseClass.Player = __instance;
            AmandsSenseClass.inventoryControllerClass = __instance.InventoryController;
            AmandsSenseClass.Clear();
            AmandsSenseClass.ReloadFiles(true);
        }
    }
}
