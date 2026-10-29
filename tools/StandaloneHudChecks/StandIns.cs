using System.Reflection;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b) || (ReferenceEquals(a, null) && b.Destroyed) || (ReferenceEquals(b, null) && a.Destroyed);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public class GameObject : Object
    {
        private readonly List<Component> components = new();
        public void Add(Component component) { components.Add(component); component.gameObject = this; }
        public Component GetComponent(Type type) => components.FirstOrDefault(c => type.IsAssignableFrom(c.GetType()));
    }
    public class Component : Object
    {
        public GameObject gameObject = new();
        public Component GetComponent(Type type) => gameObject.GetComponent(type);
    }
    public class MonoBehaviour : Component { }
    public static class Time { public static float realtimeSinceStartup; }
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
    public sealed class Version { } // Match the real game's collision with System.Version.
    public enum GameStatus { Starting, Started, Running, Stopping, Stopped }
    public class AbstractGame { }
    public class Profile { public string Id = "same-profile"; public List<EFT.Quests.Quest> QuestsData = new(); }
    public class LocalGame : AbstractGame { public GameWorld GameWorld; public LocalPlayer LocalPlayer; public Profile Profile; public GameStatus Status = GameStatus.Running; }
    public interface IPlayer { }
    public class Health { public bool IsAlive = true; }
    public class Player : UnityEngine.MonoBehaviour, IPlayer
    {
        public GameWorld GameWorld; public Profile Profile = new(); public bool IsYourPlayer = true;
        public EFT.Quests.QuestController QuestController = new();
        public Health HealthController = new(); public readonly object Movement = new(), Physical = new(), Stamina = new();
        public int HealthValue = 100;
    }
    public class LocalPlayer : Player { }
    public class GameWorld : UnityEngine.MonoBehaviour
    {
        public Player MainPlayer; public readonly List<IPlayer> RegisteredPlayers = new();
        public virtual void UnregisterPlayer(IPlayer iPlayer) => RegisteredPlayers.Remove(iPlayer);
        public void RegisterPlayer(Player body) { if (!RegisteredPlayers.Contains(body)) RegisteredPlayers.Add(body); if (body.IsYourPlayer) MainPlayer = body; }
    }
    public class HideoutGameWorld : GameWorld { }
    public sealed class LocalGameWorld : ClientGameWorld { }
    public class ClientGameWorld : GameWorld
    {
        public override void UnregisterPlayer(IPlayer iPlayer)
        {
            if (RegisteredPlayers.Contains(iPlayer)) base.UnregisterPlayer(iPlayer);
        }
    }
    public static class GamePlayerOwner { public static Player MyPlayer; }
}
namespace EFT.InventoryLogic { public class Weapon { } }
namespace EFT.Quests
{
    public class QuestTemplate { public string LocationId = "map"; }
    public class Quest { public string Id; public QuestTemplate Template = new(); public int Status = 2; public int Progress; }
    public class QuestCollection : List<Quest> { public Quest GetConditional(string id) => this.FirstOrDefault(q => q.Id == id); }
    public class QuestController { public QuestCollection Quests = new(); }
}
namespace EFT.Vehicle { public class BTRSide { } }
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
        public static void Publish(EFT.Player oldBody, EFT.Player body) => Replaced?.Invoke(oldBody, body, "swap");
    }
}
