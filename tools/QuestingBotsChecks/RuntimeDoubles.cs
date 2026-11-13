using System.Runtime.CompilerServices;
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
namespace Redeploy.Core.Engine { public static class RespawnEngineState { public static bool IsPipelineRunning; } }
namespace Redeploy.Core.Api
{
    public static class RespawnApi
    {
        public static event Action<EFT.Player, EFT.Player, string> Replaced;
        public static void Publish(EFT.Player oldBody, EFT.Player body) => Replaced?.Invoke(oldBody, body, "swap");
    }
}
namespace UnityEngine
{
    public struct Vector3 { public float x; }
    public class Component
    {
        public GameObject gameObject; public bool Destroyed;
        public static bool operator ==(Component a, Component b) => ReferenceEquals(a, b) || ((ReferenceEquals(a, null) || a.Destroyed) && (ReferenceEquals(b, null) || b.Destroyed));
        public static bool operator !=(Component a, Component b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }
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
    public interface IPlayer { Profile Profile { get; } }
    public class Player : UnityEngine.Component, IPlayer
    {
        public bool IsYourPlayer = true, IsAI, OnIsland;
        public GameWorld GameWorld;
        public UnityEngine.Vector3 Position => new() { x = OnIsland ? 1 : 0 };
        public Profile Profile { get; set; } = new();
        public Health HealthController = new(); public AIData AIData;
    }
    public class AIData { public BotOwner BotOwner; }
    public class BotOwner { public Player GetPlayer; public BotsGroup BotsGroup = new(); }
    public class LocalPlayer : Player { }
    public class GameWorld : UnityEngine.MonoBehaviour
    {
        public GameWorld() { gameObject = new(); }
        public Player MainPlayer;
        public readonly List<IPlayer> RegisteredPlayers = new();
        public readonly List<Player> AllAlivePlayersList = new();
        [MethodImpl(MethodImplOptions.NoInlining)] public virtual void UnregisterPlayer(IPlayer player)
        { RegisteredPlayers.Remove(player); AllAlivePlayersList.Remove(player as Player); }
        public void Register(Player player) { RegisteredPlayers.Add(player); AllAlivePlayersList.Add(player); if (player.IsYourPlayer) MainPlayer = player; }
    }
    public class HideoutGameWorld : GameWorld { }
    public class ClientGameWorld : GameWorld
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public override void UnregisterPlayer(IPlayer player)
        { if (RegisteredPlayers.Contains(player)) base.UnregisterPlayer(player); }
    }
    public class AbstractGame { }
    public enum GameStatus { Started, Starting, Running, Stopped }
    public class LocalGame : AbstractGame { public LocalPlayer LocalPlayer; public GameWorld GameWorld; public Profile Profile; public GameStatus Status = GameStatus.Started; }
}
public class BotsGroup
{
    public List<EFT.IPlayer> Allies { get; } = new();
    public Dictionary<EFT.IPlayer, object> Enemies { get; } = new();
    public int NullAllyAdds;
    public void AddAlly(EFT.Player player) { if (player == null) NullAllyAdds++; if (!Allies.Contains(player)) Allies.Add(player); }
}
