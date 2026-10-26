using System.Runtime.CompilerServices;
namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)] public class BepInPlugin : Attribute
    { public string GUID { get; } public Version Version { get; } public BepInPlugin(string guid, string name, string version) { GUID = guid; Version = new Version(version); } }
    public class BaseUnityPlugin { }
    public class PluginInfo { public BaseUnityPlugin Instance; public BepInPlugin Metadata; }
}
namespace BepInEx.Bootstrap { public static class Chainloader { public static readonly Dictionary<string, BepInEx.PluginInfo> PluginInfos = new(); } }
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; public static bool Instantiated => Instance != null; } }
namespace Redeploy.Core { public static class CoreLog { public static int Warnings; public static void LogWarning(string message) { Warnings++; Console.WriteLine(message); } public static void LogDebug(string message) { } } }
namespace EFT
{
    public interface IPlayer { }
    public class Player : IPlayer
    {
        public bool IsYourPlayer = true; public GameWorld GameWorld; public object Profile = new();
        public class MedsController { public Player _player; }
    }
    public class LocalPlayer : Player { }
    public class GameWorld
    {
        public Player MainPlayer; public int UnregisterCount;
        [MethodImpl(MethodImplOptions.NoInlining)] public virtual void UnregisterPlayer(IPlayer player) { UnregisterCount++; }
    }
    public class ClientWorld : GameWorld
    {
        // Actual client override may return before base after native death already unregistered.
        [MethodImpl(MethodImplOptions.NoInlining)] public override void UnregisterPlayer(IPlayer player) { UnregisterCount++; }
    }
    public class HideoutGameWorld : GameWorld { }
    public class AbstractGame { }
    public enum GameStatus { Started, Starting, Running, Stopped }
    public class LocalGame : AbstractGame { public Player LocalPlayer; public GameWorld GameWorld; public object Profile; public GameStatus Status = GameStatus.Started; }
}
