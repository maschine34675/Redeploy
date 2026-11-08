using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b)
            => ReferenceEquals(a, b) || ((ReferenceEquals(a, null) || a.Destroyed) && (ReferenceEquals(b, null) || b.Destroyed));
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();
    }
    public class MonoBehaviour : Object { }
    public struct Vector3 { }
}
namespace BepInEx
{
    public class BaseUnityPlugin : UnityEngine.MonoBehaviour { }
    public sealed class BepInPlugin : Attribute
    {
        public string GUID { get; }
        public Version Version { get; }
        public BepInPlugin(string guid, string name, string version) { GUID = guid; Version = Version.Parse(version); }
    }
    public class PluginInfo { public BaseUnityPlugin Instance; public BepInPlugin Metadata; }
}
namespace BepInEx.Bootstrap
{
    public static class Chainloader { public static readonly Dictionary<string, BepInEx.PluginInfo> PluginInfos = new(); }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace EFT
{
    public enum GameStatus { Running, Starting, Started, Stopping }
    public enum EDamageType { Bullet }
    public class MovementContext { }
    public class SkillManager
    {
        public class FloatBuff { public float Value; }
        public FloatBuff Endurance = new(), Strength = new();
    }
    public class Profile { public string Id; public SkillManager Skills = new(); }
    public class AbstractGame { }
    public class LocalGame : AbstractGame { public GameWorld GameWorld; public LocalPlayer LocalPlayer; public Profile Profile; public GameStatus Status; }
    public class Player : UnityEngine.MonoBehaviour
    {
        public GameWorld GameWorld; public Profile Profile; public bool IsYourPlayer;
        public string ProfileId => Profile.Id;
        public MovementContext MovementContext = new();
        public SkillManager Skills => Profile.Skills;
        public HealthSystem.ActiveHealthController HealthController;
    }
    public class LocalPlayer : Player { }
    public class GameWorld : UnityEngine.MonoBehaviour { public Player MainPlayer; public List<Player> RegisteredPlayers = new(); }
    public class HideoutGameWorld : GameWorld { }
    public static class GamePlayerOwner { public static Player MyPlayer; }
}
namespace EFT.HealthSystem
{
    public class ActiveHealthController
    {
        public EFT.Player Player;
        public bool IsAlive = true;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Kill(EFT.EDamageType damageType) => IsAlive = false;
    }
}
namespace Redeploy.Core
{
    public static class CoreLog { public static void LogWarning(string text) => Console.WriteLine(text); }
}
namespace Redeploy.Core.Api
{
    public static class RespawnApi
    {
        public static event Action<EFT.Player, EFT.Player, string> Replaced;
        public static event Action<EFT.Player, string> ReplacementFailed;
        public static void Replace(EFT.Player oldBody, EFT.Player newBody) => Replaced?.Invoke(oldBody, newBody, "death");
        public static void Fail(EFT.Player oldBody) => ReplacementFailed?.Invoke(oldBody, "rewire");
    }
}
