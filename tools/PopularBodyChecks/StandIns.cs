using System.Runtime.CompilerServices;

namespace UnityEngine
{
    public partial class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b) || (ReferenceEquals(a, null) && b.Destroyed) || (ReferenceEquals(b, null) && a.Destroyed);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }
    public class GameObject : Object
    {
        private readonly List<Component> components = new();
        public static int Enumerations;
        public void Add(Component component) { components.Add(component); component.gameObject = this; }
        public Component[] GetComponents(Type type) { Enumerations++; return components.Where(c => type.IsAssignableFrom(c.GetType())).ToArray(); }
    }
    public class Component : Object
    {
        private GameObject owner = new();
        public GameObject gameObject
        {
            get { if (Destroyed) throw new InvalidOperationException("destroyed native component gameObject access"); return owner; }
            set => owner = value;
        }
    }
    public class Behaviour : Component { public bool enabled = true; }
    public class MonoBehaviour : Behaviour { }
}
namespace BepInEx
{
    public class BaseUnityPlugin : UnityEngine.MonoBehaviour { }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BepInPlugin : Attribute
    {
        public string GUID { get; } public Version Version { get; }
        public BepInPlugin(string guid, string name, string version) { GUID = guid; Version = System.Version.Parse(version); }
    }
    public class PluginInfo { public BaseUnityPlugin Instance; public BepInPlugin Metadata; }
}
namespace BepInEx.Bootstrap { public static class Chainloader { public static readonly Dictionary<string, BepInEx.PluginInfo> PluginInfos = new(); } }
namespace Comfort.Common { public static class Singleton<T> where T : class { public static T Instance; public static bool Instantiated => Instance != null; } }
namespace EFT
{
    public sealed class Version { }
    public enum GameStatus { Starting, Started, Running, Stopping, Stopped }
    public class AbstractGame { }
    public class Profile { public string Id = "same-profile"; }
    public class LocalGame : AbstractGame { public GameWorld GameWorld; public LocalPlayer LocalPlayer; public Profile Profile; public GameStatus Status = GameStatus.Running; }
    public interface IPlayer { }
    public class Health { public bool IsAlive = true; }
    public partial class Player : UnityEngine.MonoBehaviour, IPlayer
    {
        public GameWorld GameWorld; public Profile Profile = new(); public bool IsYourPlayer = true;
        public Health HealthController = new(); public int HandResets;
    }
    public class LocalPlayer : Player { }
    public partial class GameWorld : UnityEngine.MonoBehaviour
    {
        public Player MainPlayer; public readonly List<IPlayer> RegisteredPlayers = new();
        public void UnregisterPlayer(IPlayer player) => RegisteredPlayers.Remove(player);
        public void RegisterPlayer(Player body) { if (!RegisteredPlayers.Contains(body)) RegisteredPlayers.Add(body); if (body.IsYourPlayer) MainPlayer = body; }
    }
    public class HideoutGameWorld : GameWorld { }
    public static class GamePlayerOwner { public static Player MyPlayer; }
}
namespace Redeploy.Core
{
    public static class CoreLog
    {
        public static readonly List<string> Warnings = new();
        public static void LogDebug(string value) { }
        public static void LogWarning(string value) => Warnings.Add(value);
    }
}
namespace Redeploy.Core.Api
{
    public static class RespawnApi
    {
        public static event Action<EFT.Player, EFT.Player, string> Replaced;
        public static event Action<EFT.Player, string> ReplacementFailed;
        public static void Publish(EFT.Player oldBody, EFT.Player body) => Replaced?.Invoke(oldBody, body, "swap");
        public static void Fail(EFT.Player oldBody) => ReplacementFailed?.Invoke(oldBody, "create");
    }
}
