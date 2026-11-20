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
namespace UnityEngine
{
    public class Object { public bool Destroyed; public static void Destroy(Object value) => value.Destroyed = true; }
    public class GameObject : Object { }
    public class AudioSource : Object { public int Stops; public void Stop() => Stops++; }
    public struct Vector3 { public float x; public static Vector3 zero => new(); }
}
namespace EFT
{
    public interface IPlayer { }
    public class Profile { }
    public class Player : IPlayer
    {
        public bool IsYourPlayer = true; public GameWorld GameWorld; public Profile Profile = new();
        public MovementContext MovementContext = new();
        public class FirearmController { public Player _player; }
    }
    public class LocalPlayer : Player { }
    public class GameWorld
    {
        public Player MainPlayer; public int UnregisterCount;
        [MethodImpl(MethodImplOptions.NoInlining)] public virtual void UnregisterPlayer(IPlayer player) { UnregisterCount++; }
    }
    public class ClientWorld : GameWorld
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public override void UnregisterPlayer(IPlayer player) { UnregisterCount++; }
    }
    public class BaseCallingWorld : GameWorld
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public override void UnregisterPlayer(IPlayer player) { base.UnregisterPlayer(player); }
    }
    public class HideoutGameWorld : GameWorld { }
    public class AbstractGame { }
    public enum GameStatus { Started, Starting, Running, Stopped }
    public class LocalGame : AbstractGame { public Player LocalPlayer; public GameWorld GameWorld; public Profile Profile; public GameStatus Status = GameStatus.Started; }
    public class MovementContext
    {
        public BaseMovementState OverridenState, CurrentState, PreviousState = new();
        public float SpeedLimit = 5; public int Exits; public bool ThrowOnExit;
        public void ExitOverridenState()
        {
            if (ThrowOnExit) throw new InvalidOperationException("injected movement exit failure");
            OverridenState = null; CurrentState.Exit(false); CurrentState = PreviousState; Exits++;
        }
    }
    public class BaseMovementState
    {
        public MovementContext MovementContext;
        public virtual void Exit(bool same) { }
    }
}
