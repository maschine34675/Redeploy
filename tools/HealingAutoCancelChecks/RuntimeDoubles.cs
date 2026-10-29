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
namespace Redeploy.Core.Api { internal static class RespawnApi { internal static event Action<EFT.Player, EFT.Player, string> Replaced; internal static void Publish(EFT.Player previous, EFT.Player next) => Replaced?.Invoke(previous, next, "test"); } }
public enum EBodyPart { Head, Chest }
public enum EDamageType { Medicine, Bullet }
namespace EFT.Ballistics { public struct DamageInfo { public EDamageType DamageType; } }
namespace EFT.HealthSystem
{
    public class ActiveHealthController
    {
        public event Action<EBodyPart, float, EFT.Ballistics.DamageInfo> HealthChangedEvent;
        public Action BeforeDispatch; public int Removed; public bool Full = true, Bleeding, Broken;
        public Delegate[] Callbacks => HealthChangedEvent?.GetInvocationList() ?? Array.Empty<Delegate>();
        [MethodImpl(MethodImplOptions.NoInlining)] public void ChangeHealth(EBodyPart part, float amount, EFT.Ballistics.DamageInfo damage)
        { var captured = HealthChangedEvent; BeforeDispatch?.Invoke(); captured?.Invoke(part, amount, damage); }
        public void RemoveMedEffect() => Removed++;
    }
}
namespace EFT
{
    public interface IPlayer { }
    public class Profile { }
    public class Player : IPlayer
    {
        public bool IsYourPlayer = true; public GameWorld GameWorld; public Profile Profile = new();
        public HealthSystem.ActiveHealthController ActiveHealthController = new(); public bool HasMeds = true; public float Resource = 100;
    }
    public class LocalPlayer : Player { }
    public class GamePlayerOwner { public static Player MyPlayer; }
    public class GameWorld
    {
        public Player MainPlayer; public List<Player> RegisteredPlayers = new();
        [MethodImpl(MethodImplOptions.NoInlining)] public virtual void UnregisterPlayer(IPlayer player) { RegisteredPlayers.Remove((Player)player); }
    }
    public class ClientWorld : GameWorld
    {
        [MethodImpl(MethodImplOptions.NoInlining)] public override void UnregisterPlayer(IPlayer player) { RegisteredPlayers.Remove((Player)player); }
    }
    // Live LocalGameWorld inherits ClientGameWorld's override without implementing it.
    public class DerivedClientWorld : ClientWorld { }
    public class HideoutGameWorld : GameWorld { }
    public class AbstractGame { }
    public enum GameStatus { Started, Starting, Running, Stopped }
    public class LocalGame : AbstractGame { public Player LocalPlayer; public GameWorld GameWorld; public Profile Profile; public GameStatus Status = GameStatus.Started; }
}
