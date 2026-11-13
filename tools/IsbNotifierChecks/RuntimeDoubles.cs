using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Bootstrap;
using EFT;

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)] public class BepInPlugin : Attribute
    { public string GUID { get; } public Version Version { get; } public BepInPlugin(string guid, string name, string version) { GUID = guid; Version = new Version(version); } }
    public class BaseUnityPlugin : UnityEngine.Object { }
    public class PluginInfo { public BaseUnityPlugin Instance; public BepInPlugin Metadata; }
}
namespace BepInEx.Configuration { public class ConfigEntry<T> { public T Value; } }
namespace BepInEx.Bootstrap { public static class Chainloader { public static readonly Dictionary<string, PluginInfo> PluginInfos = new(); } }
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; public static bool Instantiated => Instance != null; } }
namespace Redeploy.Core { public static class CoreLog { public static int Warnings; public static void LogWarning(string message) { Warnings++; Console.WriteLine(message); } public static void LogDebug(string message) { } } }
namespace Redeploy.Core.Api
{
    public static class RespawnApi
    {
        public static event Action<Player, Player, string> Replaced;
        public static event Action<Player, string> ReplacementFailed;
        public static void Publish(Player old, Player next) => Replaced?.Invoke(old, next, "death");
        public static void Fail(Player old, string stage) => ReplacementFailed?.Invoke(old, stage);
    }
}
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object left, Object right)
        { bool l = ReferenceEquals(left, null) || left.Destroyed, r = ReferenceEquals(right, null) || right.Destroyed; return l || r ? l && r : ReferenceEquals(left, right); }
        public static bool operator !=(Object left, Object right) => !(left == right);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }
}
namespace EFT
{
    public class Profile { }
    public class HealthController { public bool IsAlive = true; }
    public class Player : UnityEngine.Object { public bool IsYourPlayer = true; public GameWorld GameWorld; public Profile Profile = new(); public HealthController HealthController = new(); }
    public class LocalPlayer : Player { }
    public class GameWorld : UnityEngine.Object { public Player MainPlayer; public List<Player> AllAlivePlayersList = new(); }
    public class HideoutGameWorld : GameWorld { }
    public class AbstractGame : UnityEngine.Object { }
    public enum GameStatus { Started, Starting, Running, Stopping, SoftStopping, Stopped }
    public class LocalGame : AbstractGame { public LocalPlayer LocalPlayer; public GameWorld GameWorld; public Profile Profile; public GameStatus Status = GameStatus.Started; }
}

// Synthetic native behavior: the real release's field/method/IL contract is checked separately.
public static class Fixture
{
    public static Type PluginType;
    public static BaseUnityPlugin Plugin;
    public static bool Headless, ThrowEnabled;
    public static int ResetCount;
    public static T Get<T>(string field) => (T)PluginType.GetField(field, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
    public static void Set(string field, object value) => PluginType.GetField(field, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, value);
    public static void Call(string name) => PluginType.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
    public static bool Enabled() { if (ThrowEnabled) throw new InvalidOperationException("synthetic predicate fault"); return Get<ConfigEntry<bool>>("EnablePlugin")?.Value != false; }
    public static bool IsHeadless() => Headless;
    public static void Mark() { Set("LocalPlayerObservedInRaid", true); Set("LocalRaidPresentationEnded", true); }
    public static void Reset() { ResetCount++; Set("LocalRaidPresentationEnded", false); Set("LocalPlayerObservedInRaid", false); Set("InitialMessageShown", false); Get<Dictionary<string, int>>("History").Clear(); }
    public static bool Present() { Refresh(); return !Get<bool>("LocalRaidPresentationEnded") && Enabled() && !Headless; }
    public static void Refresh()
    {
        if (Get<bool>("LocalRaidPresentationEnded") || Headless) return;
        var world = Comfort.Common.Singleton<GameWorld>.Instance;
        var player = world?.MainPlayer;
        if (player != null && player.HealthController?.IsAlive == true && world.AllAlivePlayersList.Any(p => ReferenceEquals(p, player)))
            Set("LocalPlayerObservedInRaid", true);
        else if (player?.HealthController?.IsAlive == false || Get<bool>("LocalPlayerObservedInRaid")) Mark();
    }
    public static void Build(string mode)
    {
        string version = mode == "older" ? "2.0.3.0" : mode == "newer" ? "2.1.0.0" : "2.0.4.0";
        string guid = mode == "main-guid" || mode == "main-only" ? "com.samc137.isbspecialforces" : "com.samc137.ISBinfo";
        var asm = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName(mode == "assembly" ? "Other" : "ISBNotify") { Version = new Version(version) }, AssemblyBuilderAccess.Run);
        var type = asm.DefineDynamicModule("Fixture").DefineType("ISBInfo.ISBInfoPlugin", TypeAttributes.Public, typeof(BaseUnityPlugin));
        type.SetCustomAttribute(new CustomAttributeBuilder(typeof(BepInPlugin).GetConstructor(new[] { typeof(string), typeof(string), typeof(string) }), new object[] { guid, "Fixture", version }));
        void Field(string name, Type fieldType) => type.DefineField(mode == "drift" && name == "LocalRaidPresentationEnded" ? "ChangedLatch" : name, fieldType, FieldAttributes.Private | FieldAttributes.Static);
        Field("LocalRaidPresentationEnded", typeof(bool)); Field("LocalPlayerObservedInRaid", typeof(bool)); Field("NextPresentationStateRefreshTime", typeof(float)); Field("EnablePlugin", typeof(ConfigEntry<bool>)); Field("Instance", type);
        Field("InitialMessageShown", typeof(bool)); Field("History", typeof(Dictionary<string, int>));
        void Forward(string name, string target, bool isPublic = false)
        {
            var native = typeof(Fixture).GetMethod(target);
            var method = type.DefineMethod(name, MethodAttributes.Static | (isPublic ? MethodAttributes.Public : MethodAttributes.Private), native.ReturnType, Type.EmptyTypes);
            method.SetImplementationFlags(MethodImplAttributes.NoInlining);
            var il = method.GetILGenerator(); il.Emit(OpCodes.Call, native); il.Emit(OpCodes.Ret);
        }
        Forward("IsPluginEnabled", nameof(Enabled)); Forward("IsHeadless", nameof(IsHeadless)); Forward("CanPresentLocally", nameof(Present)); Forward("RefreshLocalRaidPresentationState", nameof(Refresh)); Forward("ResetState", nameof(Reset)); Forward("MarkLocalRaidPresentationEnded", nameof(Mark), true);
        PluginType = type.CreateType(); Plugin = (BaseUnityPlugin)Activator.CreateInstance(PluginType);
        Set("Instance", Plugin); Set("EnablePlugin", new ConfigEntry<bool> { Value = true }); Set("History", new Dictionary<string, int> { ["commander"] = 7 }); Set("InitialMessageShown", true);
        Chainloader.PluginInfos[mode == "main-only" ? guid : "com.samc137.ISBinfo"] = new PluginInfo { Instance = Plugin, Metadata = PluginType.GetCustomAttribute<BepInPlugin>() };
    }
}
