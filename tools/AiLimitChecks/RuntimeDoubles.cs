namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)] public class BepInPlugin : Attribute
    { public string GUID { get; } public Version Version { get; } public BepInPlugin(string guid, string name, string version) { GUID = guid; Version = new Version(version); } }
    public class BaseUnityPlugin : UnityEngine.MonoBehaviour { }
    public class PluginInfo { public BaseUnityPlugin Instance; public BepInPlugin Metadata; }
}
namespace BepInEx.Bootstrap { public static class Chainloader { public static readonly Dictionary<string, BepInEx.PluginInfo> PluginInfos = new(); } }
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; public static bool Instantiated => Instance != null; } }
namespace Redeploy.Core { public static class CoreLog { public static int Warnings; public static void LogWarning(string message) { Warnings++; Console.WriteLine(message); } } }
namespace UnityEngine
{
    public class Component { public GameObject gameObject; }
    public class MonoBehaviour : Component { }
    public class GameObject
    {
        public readonly List<Component> Components = new();
        public Component[] GetComponents(Type type) => Components.Where(type.IsInstanceOfType).ToArray();
    }
}
namespace EFT
{
    public class Profile { public string Id = "same-profile"; }
    public class Health { public bool IsAlive = true; }
    public class Player
    {
        public bool IsYourPlayer = true, IsAI; public GameWorld GameWorld; public Profile Profile = new();
        public Health HealthController = new(); public float Position;
    }
    public class LocalPlayer : Player { }
    public class GameWorld : UnityEngine.MonoBehaviour
    {
        public GameWorld() { gameObject = new(); }
        public Player MainPlayer; public readonly List<Player> RegisteredPlayers = new();
    }
    public class HideoutGameWorld : GameWorld { }
    public class AbstractGame { }
    public enum GameStatus { Started, Starting, Running, Stopped }
    public class LocalGame : AbstractGame { public Player LocalPlayer; public GameWorld GameWorld; public Profile Profile; public GameStatus Status = GameStatus.Started; }
    public static class GamePlayerOwner { public static Player MyPlayer; }
}
namespace Redeploy.Core.Api
{
    public static class RespawnApi
    {
        public static event Action<EFT.Player, EFT.Player, string> Replaced;
        public static event Action<EFT.Player, string> ReplacementFailed;
        public static void Publish(EFT.Player oldBody, EFT.Player next) => Replaced?.Invoke(oldBody, next, "test");
        public static void Fail(EFT.Player oldBody) => ReplacementFailed?.Invoke(oldBody, "rewire");
    }
}
