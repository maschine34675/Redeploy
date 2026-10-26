// External Unity/EFT/BepInEx boundaries only. Ownership and adapter decisions are production sources.
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public string name => Destroyed ? throw new NullReferenceException("destroyed Unity name") : "fixture";
        public static void Destroy(Object value) { if (value != null) value.Destroyed = true; }
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b)
            || (ReferenceEquals(a, null) && !ReferenceEquals(b, null) && b.Destroyed)
            || (ReferenceEquals(b, null) && !ReferenceEquals(a, null) && a.Destroyed);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public static implicit operator bool(Object value) => value != null;
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component => gameObject.GetComponentsInChildren<T>(inactive);
        public T GetComponentInParent<T>(bool includeInactive) where T : Component
        {
            for (var t = transform; t != null; t = t.parent) if (t.gameObject.GetComponent(typeof(T)) is T value) return value;
            return null;
        }
    }
    public class MonoBehaviour : Component { }
    public class GameObject : Object
    {
        public readonly Transform transform;
        public GameObject() { transform = new Transform { gameObject = this }; }
        public bool activeSelf = true;
        public void SetActive(bool value) => activeSelf = value;
        private readonly List<Component> _components = new();
        public T AddComponent<T>() where T : Component, new() { var c = new T { gameObject = this }; _components.Add(c); return c; }
        public Component AddComponent(Type type) { var c = (Component)Activator.CreateInstance(type, true); c.gameObject = this; _components.Add(c); return c; }
        public Component GetComponent(Type type) => _components.SingleOrDefault(type.IsInstanceOfType);
        public void Attach(Component component) { component.gameObject = this; _components.Add(component); }
        public Component[] GetComponents(Type type) => _components.Where(type.IsInstanceOfType).ToArray();
        public T[] GetComponentsInChildren<T>(bool inactive) where T : Component => _components.OfType<T>().Concat(transform.Children.SelectMany(c => c.gameObject.GetComponentsInChildren<T>(inactive))).ToArray();
    }
    public class Transform : Component
    {
        public System.Numerics.Quaternion rotation;
        private Transform _parent;
        public readonly List<Transform> Children = new();
        public Transform parent { get => _parent; set { _parent?.Children.Remove(this); _parent = value; value?.Children.Add(this); } }
        public bool IsChildOf(Transform root) => ReferenceEquals(this, root) || (parent?.IsChildOf(root) ?? false);
    }
    public class Mesh : Object { }
    public class Renderer : Component { public bool forceRenderingOff; }
    public class MeshFilter : Component { public Mesh sharedMesh; }
    public class Camera : Component { public static Camera main; }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace EFT
{
    public enum GameStatus { Starting, Started, Running, Stopping, Stopped }
    public class Profile { public string Id = "same-profile"; }
    public class AbstractGame : UnityEngine.Object { }
    public class LocalGame : AbstractGame { public GameWorld GameWorld; public LocalPlayer LocalPlayer; public Profile Profile; public GameStatus Status = GameStatus.Running; }
    public interface IPlayer { event Action<IPlayer> OnIPlayerDeadOrUnspawn; }
    public class Player : UnityEngine.Component, IPlayer
    {
        public Player() { gameObject = new UnityEngine.GameObject(); gameObject.Attach(this); }
        public MovementContext MovementContext = new();
        public object InventoryController = new();
        public HandsControllerBase HandsController;
        public class HandsControllerBase { public UnityEngine.GameObject ControllerGameObject; }
        public class FirearmController : HandsControllerBase { public EFT.InventoryLogic.Weapon Item; }
        public GameWorld GameWorld; public bool IsYourPlayer; public Profile Profile;
        public event Action<IPlayer> OnIPlayerDeadOrUnspawn;
        public event Action<IPlayer> OnPlayerDeadOrUnspawn;
        public int InterfaceHandlers => OnIPlayerDeadOrUnspawn?.GetInvocationList().Length ?? 0;
        public void RaiseGone() { OnPlayerDeadOrUnspawn?.Invoke(this); OnIPlayerDeadOrUnspawn?.Invoke(this); }
    }
    public class LocalPlayer : Player { }
    public class MovementContext { }
    public class GameWorld : UnityEngine.Component
    {
        public Player MainPlayer;
        public List<IPlayer> RegisteredPlayers = new();
        public event Action<IPlayer> OnPersonAdd;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void OnGameStarted() { }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public virtual void UnregisterPlayer(IPlayer body) => RegisteredPlayers.Remove(body);
        public void RegisterPlayer(Player body) { RegisteredPlayers.Add(body); if (body.IsYourPlayer) MainPlayer = body; OnPersonAdd?.Invoke(body); }
    }
    public class HideoutGameWorld : GameWorld { }
    public sealed class LocalGameWorld : ClientGameWorld { }
    public class ClientGameWorld : GameWorld
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public override void UnregisterPlayer(IPlayer body)
        {
            if (RegisteredPlayers.Contains(body)) base.UnregisterPlayer(body);
        }
    }
}
namespace EFT.CameraControl
{
    public class OpticSight : UnityEngine.Component
    {
        public int Calls;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void OnEnable() { Calls++; }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void OnDisable() { Calls++; }
    }
    public class PlayerCameraController { public EFT.Player Player; }
    public class CameraManager { public static CameraManager Instance = new(); public UnityEngine.Camera Camera; }
}
namespace EFT.InventoryLogic { public class Weapon { } }
namespace BepInEx
{
    public class BaseUnityPlugin : UnityEngine.MonoBehaviour { }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BepInPlugin(string guid, string name, string version) : Attribute
    { public string GUID = guid; public string Name = name; public Version Version = new(version); }
    public class PluginInfo { public BaseUnityPlugin Instance; public BepInPlugin Metadata; }
}
namespace BepInEx.Bootstrap
{
    public static class Chainloader { public static Dictionary<string, BepInEx.PluginInfo> PluginInfos = new(); }
}
namespace Redeploy.Core
{
    public static class CoreLog
    {
        public static readonly List<string> Warnings = new();
        public static void LogWarning(string value) => Warnings.Add(value);
        public static void LogDebug(string value) { }
    }
}
