using System.Runtime.CompilerServices;
namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)]
    public class BepInPlugin : Attribute
    {
        public string GUID
        {
            get;
        }
        public Version Version
        {
            get;
        }
        public BepInPlugin(string guid, string name, string version)
        {
            GUID = guid;
            Version = new Version(version);
        }
    }
    public class BaseUnityPlugin : UnityEngine.MonoBehaviour
    {
    }
    public class PluginInfo
    {
        public BaseUnityPlugin Instance; public BepInPlugin Metadata;
    }
}
namespace BepInEx.Bootstrap
{
    public static class Chainloader
    {
        public static readonly Dictionary<string, BepInEx.PluginInfo> PluginInfos = new();
    }
}
namespace Comfort.Common
{
    public static class Singleton<T>
    {
        public static T Instance; public static bool Instantiated => Instance != null;
    }
}
namespace Redeploy.Core
{
    public static class CoreLog
    {
        public static int Warnings; public static void LogWarning(string text)
        {
            Warnings++;
            Console.WriteLine(text);
        }
        public static void LogDebug(string text)
        {
        }
    }
}
namespace Redeploy.Core.Api
{
    public static class RespawnApi
    {
        public static event Action<EFT.Player, EFT.Player, string> Replaced; public static void Publish(EFT.Player oldBody, EFT.Player newBody) => Replaced?.Invoke(oldBody, newBody, "test");
    }
}
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object left, Object right)
            => (ReferenceEquals(left, null) || left.Destroyed)
                ? ReferenceEquals(right, null) || right.Destroyed : ReferenceEquals(left, right);
        public static bool operator !=(Object left, Object right) => !(left == right);
        public override bool Equals(object value) => ReferenceEquals(this, value);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }
}
namespace EFT
{
    public class Profile
    {
    }
    public class Player : UnityEngine.Object
    {
        public UnityEngine.GameObject gameObject = new(); public bool IsYourPlayer = true; public GameWorld GameWorld; public Profile Profile = new(); public object HandsController;
        public class AbstractHandsController {}
        public class ItemHandsController : AbstractHandsController { public Player _player; }
        public class FirearmController : ItemHandsController
        {
            public EFT.InventoryLogic.Weapon Weapon; public bool autoFireOn;
        }
    }
    public class LocalPlayer : Player
    {
    }
    public class GameWorld
    {
        public Player MainPlayer;
    }
    public class HideoutGameWorld : GameWorld
    {
    }
    public class AbstractGame
    {
    }
    public enum GameStatus
    {
        Starting, Started, Running, Stopped
    }
    public class LocalGame : AbstractGame
    {
        public Player LocalPlayer; public GameWorld GameWorld; public Profile Profile; public GameStatus Status = GameStatus.Started;
    }
}
namespace EFT.InventoryLogic
{
    public class Weapon
    {
        public string TemplateId;
    }
}
namespace EFT.Animations
{
    public class ProceduralWeaponAnimation
    {
        public EFT.Player.FirearmController _firearmController;
    }
}

namespace UnityEngine {
 public class GameObject : Object { public T AddComponent<T>() where T:Behaviour,new() { var x=new T{gameObject=this}; var awake=typeof(T).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic); awake?.Invoke(x,null); return x; } }
 public class Behaviour : Object { public GameObject gameObject; public bool enabled=true; }
 public class MonoBehaviour : Behaviour { }
}
public class Physical { public EFT.Player _player; }
namespace EFT { public class MovementContext { public Player _player; } public class MovementState { public MovementContext MovementContext; } }
