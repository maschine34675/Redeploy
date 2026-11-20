// External Unity/EFT/BepInEx boundaries only. Ownership and adapter decisions are production sources.
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public string name => Destroyed ? throw new NullReferenceException("destroyed Unity name") : "fixture";
                public static int DestroyCalls;
        public static void DestroyImmediate(Object value)
        {
            if (value == null) return;
            DestroyCalls++;
            value.GetType().GetMethod("OnDestroy", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(value, null);
            value.Destroyed = true;
            if (value is GameObject go) foreach (var component in go.GetComponents(typeof(Component))) DestroyImmediate(component);
        }
        public static void Destroy(Object value) => DestroyImmediate(value);
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
    }
    public class MonoBehaviour : Component { }
    public class GameObject : Object
    {
        public static bool SwallowAwakeExceptions;
        public readonly Transform transform = new();
        private readonly List<Component> _components = new();
        public T AddComponent<T>() where T : Component, new() => (T)AddComponent(typeof(T));
        public Component AddComponent(Type type) { var c = (Component)Activator.CreateInstance(type, true); c.gameObject = this; _components.Add(c); try { type.GetMethod("Awake")?.Invoke(c, null); } catch when (SwallowAwakeExceptions) { } return c; }
        public Component GetComponent(Type type) => _components.Where(c => c != null).SingleOrDefault(type.IsInstanceOfType);
        public Component[] GetComponents(Type type) => _components.Where(c => c != null && type.IsInstanceOfType(c)).ToArray();
    }
    public class Transform { public System.Numerics.Quaternion rotation; }
        public class Camera : Component { public static Camera main; }
    public class Material : Object { }
    public class Renderer : Component { public static readonly Material SharedDefault = new(); public bool enabled = true; public Material sharedMaterial = SharedDefault; }
    public class LineRenderer : Renderer { public int positionCount; }
    public record struct Vector3(float X, float Y, float Z);
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class { public static T Instance { get; set; } public static bool Instantiated => Instance != null; public static void Release(T value) => Instance = null; }
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
                public Player() { gameObject = new UnityEngine.GameObject(); }
        public GameWorld GameWorld; public bool IsYourPlayer; public Profile Profile;
        public Health HealthController = new();
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void OnDead(int damageType) { HealthController.IsAlive = false; }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Dispose() { }
        public event Action<IPlayer> OnIPlayerDeadOrUnspawn;
        public event Action<IPlayer> OnPlayerDeadOrUnspawn;
        public int InterfaceHandlers => OnIPlayerDeadOrUnspawn?.GetInvocationList().Length ?? 0;
        public void RaiseGone() { OnPlayerDeadOrUnspawn?.Invoke(this); OnIPlayerDeadOrUnspawn?.Invoke(this); }
    }
    public class LocalPlayer : Player { }
    public class Health { public bool IsAlive = true; }
    public class GamePlayerOwner { public static Player MyPlayer; }
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
    public class PlayerCameraController { public EFT.Player Player; }
    public class CameraManager { public static CameraManager Instance = new(); public UnityEngine.Camera Camera; }
}
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
namespace BepInEx.Configuration { public class ConfigEntry<T> { public T Value { get; set; } } }
namespace Redeploy.Core.Api
{
    public static class RespawnApi
    {
        public static event Action<EFT.Player,EFT.Player,string> Replaced;
        public static void Raise(EFT.Player oldBody, EFT.Player body) => Replaced?.Invoke(oldBody, body, "fixture");
    }
}
