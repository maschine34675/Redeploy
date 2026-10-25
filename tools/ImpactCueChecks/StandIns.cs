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
    public class MonoBehaviour : Object { }
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
    public class LocalGame : AbstractGame { public GameWorld GameWorld; public LocalPlayer LocalPlayer; public object Profile; public GameStatus Status = GameStatus.Running; }
    public interface IPlayer { }
    public class Health { public bool IsAlive = true; }
    public class Player : UnityEngine.MonoBehaviour, IPlayer
    {
        public GameWorld GameWorld; public object Profile = new(); public bool IsYourPlayer = true;
        public Health HealthController = new(); public readonly object Movement = new(), Physical = new(), Stamina = new();
        public int HealthValue = 100;
    }
    public class LocalPlayer : Player { }
    public class GameWorld : UnityEngine.MonoBehaviour
    {
        public Player MainPlayer; public readonly List<IPlayer> RegisteredPlayers = new();
        public virtual void UnregisterPlayer(IPlayer body) => RegisteredPlayers.Remove(body);
        public void RegisterPlayer(Player body) { if (!RegisteredPlayers.Contains(body)) RegisteredPlayers.Add(body); if (body.IsYourPlayer) MainPlayer = body; }
    }
    public class HideoutGameWorld : GameWorld { }
    public sealed class LocalGameWorld : ClientGameWorld { }
    public class ClientGameWorld : GameWorld
    {
        public override void UnregisterPlayer(IPlayer body)
        {
            if (RegisteredPlayers.Contains(body)) base.UnregisterPlayer(body);
        }
    }
    public static class GamePlayerOwner { public static Player MyPlayer; }
}
namespace EFT.InventoryLogic { public class Weapon { } }
namespace EFT.Vehicle { public class BTRSide { } }
namespace HarmonyLib
{
    public static class Priority { public const int First = 800; }
    public class HarmonyMethod
    {
        public MethodInfo method; public int priority;
        public HarmonyMethod(Type type, string name) => method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
    }
    public class Patch { public string owner; public MethodInfo PatchMethod; public int priority; }
    public class Patches { public readonly List<Patch> Prefixes = new(), Postfixes = new(); }
    public class Harmony
    {
        public readonly string Id;
        public static readonly Dictionary<MethodBase, Patches> Bound = new();
        public Harmony(string id) => Id = id;
        public static Patches GetPatchInfo(MethodBase target) => Bound.TryGetValue(target, out var value) ? value : null;
        public void Patch(MethodBase target, HarmonyMethod prefix = null, HarmonyMethod postfix = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (target.ReflectedType != target.DeclaringType)
                throw new InvalidOperationException("Inherited MethodInfo must be resolved to its declaration before patching");
            if (!Bound.TryGetValue(target, out var value)) Bound[target] = value = new Patches();
            if (prefix != null) value.Prefixes.Add(new Patch { owner = Id, PatchMethod = prefix.method, priority = prefix.priority });
            if (postfix != null) value.Postfixes.Add(new Patch { owner = Id, PatchMethod = postfix.method, priority = postfix.priority });
        }
        public void UnpatchSelf() { foreach (var patches in Bound.Values) { patches.Prefixes.RemoveAll(p => p.owner == Id); patches.Postfixes.RemoveAll(p => p.owner == Id); } }
    }
}
namespace Redeploy.Core
{
    internal static class CoreLog
    {
        internal static readonly List<string> Warnings = new();
        internal static void LogDebug(string value) { }
        internal static void LogWarning(string value) => Warnings.Add(value);
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
