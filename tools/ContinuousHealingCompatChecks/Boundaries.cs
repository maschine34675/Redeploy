using System.Reflection;

namespace BepInEx
{
    public class BaseUnityPlugin { }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BepInPlugin(string guid, string name, string version) : Attribute
    {
        public string GUID => guid;
        public string Name => name;
        public Version Version => new(version);
    }
    public sealed class PluginInfo { public BaseUnityPlugin Instance; public BepInPlugin Metadata; }
}
namespace BepInEx.Bootstrap
{
    public static class Chainloader { public static readonly Dictionary<string, BepInEx.PluginInfo> PluginInfos = new(); }
}
namespace HarmonyLib
{
    public static class Priority { public const int First = 800; }
    public sealed class HarmonyMethod(Type type, string name)
    {
        public int priority;
        public MethodInfo Method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
    }
    public sealed class Patch { public string owner; public MethodInfo PatchMethod; }
    public sealed class PatchInfo
    {
        public List<Patch> Prefixes = new(), Postfixes = new(), Transpilers = new(), Finalizers = new();
    }
    public sealed class Harmony(string id)
    {
        public string Id => id;
        public static readonly Dictionary<MethodBase, PatchInfo> Bound = new();
        public static bool FailUnpatch, DropReceipt;
        public static int FailPatchAt, PatchCalls;
        public void Patch(MethodInfo target, HarmonyMethod prefix)
        {
            if (!Bound.TryGetValue(target, out var info)) Bound[target] = info = new();
            if (!DropReceipt) info.Prefixes.Add(new() { owner = id, PatchMethod = prefix.Method });
            if (++PatchCalls == FailPatchAt) throw new InvalidOperationException("injected partial-patch failure");
        }
        public void UnpatchSelf()
        {
            if (FailUnpatch) throw new InvalidOperationException("injected unpatch failure");
            foreach (var info in Bound.Values) info.Prefixes.RemoveAll(p => p.owner == id);
        }
        public static PatchInfo GetPatchInfo(MethodBase target) => Bound.GetValueOrDefault(target);
    }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    {
        public static T Instance;
        public static bool Instantiated => Instance != null;
    }
}
namespace EFT
{
    public interface IBotGame { }
    public class AbstractGame { }
    public enum GameStatus { Running, Starting, Started, Stopped }
    public class LocalGame : AbstractGame, IBotGame
    {
        public GameWorld GameWorld;
        public LocalPlayer LocalPlayer;
        public object Profile = new();
        public GameStatus Status = GameStatus.Running;
    }
    public class HideoutGame : AbstractGame { }
    public class GameWorld { public Player MainPlayer; }
    public class HideoutGameWorld : GameWorld { }
    public class LocalPlayer : Player { }
    public class Player
    {
        public GameWorld GameWorld;
        public object Profile;
        public bool IsYourPlayer;
        public Health HealthController = new();
        public class MedsController
        {
            public Player _player;
            public void Spawn(float animationSpeed, Action callback) { }
            public class MedsInHandsOperation
            {
                public MedsController _controller;
                public int ForeignRuns, NativeRuns, Callbacks;
                public void OnEffectRemoved(HealthSystem.IHealthEffect effect) { NativeRuns++; if (effect is HealthSystem.MedEffect) Callbacks++; }
            }
        }
    }
    public class Health
    {
        public bool Alive = true, ThrowRead;
        public bool IsAlive => ThrowRead ? throw new InvalidOperationException("injected health getter fault") : Alive;
    }
}
namespace EFT.HealthSystem
{
    public interface IHealthEffect { }
    public sealed class MedEffect : IHealthEffect { }
    public sealed class OtherEffect : IHealthEffect { }
    public class PlayerHealthController { public void CancelApplyingItem() { } }
}
namespace Redeploy.Core
{
    public static class CoreLog
    {
        public static readonly List<string> Debug = new(), Warnings = new();
        public static void LogDebug(string text) => Debug.Add(text);
        public static void LogWarning(string text) => Warnings.Add(text);
    }
}
namespace Redeploy.Core.Compat
{
    public static class BodyReplacementCompatibility
    {
        public static Func<EFT.Player, bool> AdditionalScope;
        internal static bool OwnsAdditionalHealingBody(EFT.Player body) => AdditionalScope?.Invoke(body) == true;
    }
}
