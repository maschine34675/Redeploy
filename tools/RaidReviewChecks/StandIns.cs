using System.Reflection;
public enum EBodyPart { Head, Chest }

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
        public T AddComponent<T>() where T : Component, new() { var value = new T(); Add(value); return value; }
        public T GetComponentInParent<T>() where T : Component => components.OfType<T>().FirstOrDefault();
    }
    public class Component : Object
    {
        private GameObject _gameObject = new();
        public bool ThrowGameObject;
        public int GameObjectReads;
        public GameObject gameObject
        {
            get { GameObjectReads++; if (ThrowGameObject) throw new InvalidOperationException("fixture stale collider gameObject"); return _gameObject; }
            set { _gameObject = value; }
        }
        public Component GetComponent(Type type) => gameObject.GetComponent(type);
    }
    public class MonoBehaviour : Component { }
    public static class Time { public static float realtimeSinceStartup; public static int frameCount; }
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
    public enum EDamageType { Bullet, Explosion }
    public enum ExitStatus { Survived, Runner, Killed, MissingInAction, Left }
    public enum EPlayerSide { Bear, Usec, Savage }
    public class AbstractGame { }
    public class Profile { public string Id = "same-profile", Nickname = "Tester"; public Info Info = new(); public List<EFT.Quests.Quest> QuestsData = new(); }
    public class Info { public string MainProfileNickname; public int Level = 10; }
    public class LocalGame : AbstractGame { public GameWorld GameWorld; public LocalPlayer LocalPlayer; public Profile Profile; public GameStatus Status = GameStatus.Running; }
    public interface IPlayer { }
    public class Health { public bool IsAlive = true; }
    public class Player : UnityEngine.MonoBehaviour, IPlayer
    {
        public GameWorld GameWorld; public Profile Profile = new(); public bool IsYourPlayer = true;
        public EFT.Quests.QuestController QuestController = new();
        public Health HealthController = new(); public readonly object Movement = new(), Physical = new(), Stamina = new();
        public int HealthValue = 100;
        public string ProfileId => Profile.Id;
        public EPlayerSide Side => Profile.Id == "scav" ? EPlayerSide.Savage : EPlayerSide.Usec;
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
namespace EFT.InventoryLogic { public class Weapon { } public class Item { public string Id; } public class ItemAddress { } }
namespace EFT.Ballistics
{
    public struct DamageInfo { }
    public class Bridge { public EFT.IPlayer iPlayer; }
    public class Shot { public Bridge Player; public UnityEngine.Component HittedBallisticCollider; }
}
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
        public static event Action<EFT.Player, string> ReplacementFailed;
        public static bool IsAvailable = true;
        public static bool Pending;
        public static bool IsRespawnPending(EFT.Player body) => Pending;
        public static void Publish(EFT.Player oldBody, EFT.Player body) => Replaced?.Invoke(oldBody, body, "swap");
        public static void Fail(EFT.Player oldBody) => ReplacementFailed?.Invoke(oldBody, "rewire");
    }
}
namespace Redeploy.Core.Engine
{
    public static class DeathInterception
    {
        public static bool Claim = true;
        public static object FindClaimant(EFT.LocalGame game) => Claim ? new object() : null;
    }
}
