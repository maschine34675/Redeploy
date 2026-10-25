using System.Reflection;

// Deliberately small external boundaries. None of these types implement the
// production ownership, cancellation, lifecycle or Arena-scope decisions.
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object left, Object right)
        {
            bool leftNull = ReferenceEquals(left, null);
            bool rightNull = ReferenceEquals(right, null);
            if (leftNull && rightNull) return true;
            if (leftNull) return right.Destroyed;
            if (rightNull) return left.Destroyed;
            return ReferenceEquals(left, right);
        }
        public static bool operator !=(Object left, Object right) => !(left == right);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public class MonoBehaviour : Object { }
    public sealed class GameObject
    {
        private readonly Dictionary<Type, MonoBehaviour> _components = new();
        public static int Adds;
        public T GetComponent<T>() where T : MonoBehaviour => _components.TryGetValue(typeof(T), out var component) ? (T)component : null;
        public T AddComponent<T>() where T : MonoBehaviour, new()
        {
            var component = new T();
            _components.Add(typeof(T), component);
            Adds++;
            return component;
        }
        public void Invoke<T>(string callback) where T : MonoBehaviour
        {
            var component = GetComponent<T>() ?? throw new InvalidOperationException("Missing component " + typeof(T).Name);
            var method = typeof(T).GetMethod(callback, BindingFlags.Instance | BindingFlags.NonPublic)
                         ?? throw new InvalidOperationException("Missing Unity callback " + callback);
            try { method.Invoke(component, null); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }
    }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class
    {
        public static T Instance;
        public static bool Instantiated => !ReferenceEquals(Instance, null);
    }
}
namespace EFT
{
    public enum GameStatus { Starting, Started, Running, Stopping, Stopped }
    public class Profile { public string Id = "shared-profile-id"; }
    public class AbstractGame : UnityEngine.Object { }
    public interface IBotGame { }
    public class GameWorld : UnityEngine.Object
    {
        public Player MainPlayer;
        public readonly UnityEngine.GameObject gameObject = new();
    }
    public class HideoutGameWorld : GameWorld { }
    public class LocalGame : AbstractGame, IBotGame
    {
        public GameWorld GameWorld;
        public LocalPlayer LocalPlayer;
        public Profile Profile;
        public GameStatus Status = GameStatus.Running;
    }
    public class Player : UnityEngine.Object
    {
        public GameWorld GameWorld;
        public Profile Profile;
        public bool IsYourPlayer;
    }
    public class LocalPlayer : Player { }
}
namespace BepInEx
{
    public sealed class PluginInfo { public UnityEngine.Object Instance; }
}
namespace BepInEx.Bootstrap
{
    public static class Chainloader { public static readonly Dictionary<string, BepInEx.PluginInfo> PluginInfos = new(); }
}
namespace Redeploy.Core
{
    internal static class CoreLog
    {
        internal static readonly List<string> Warnings = new();
        internal static void LogWarning(string value) => Warnings.Add(value);
    }
}
namespace Redeploy.Core.Api
{
    public static class RespawnApi
    {
        public static event Action RaidEnded;
        internal static int SubscriberCount => RaidEnded?.GetInvocationList().Length ?? 0;
        internal static void RaiseEnded() => RaidEnded?.Invoke();
    }
}
namespace Redeploy.Core.Engine
{
    public static class RaidLifecycle
    {
        public static event Action<EFT.GameWorld> OnRaidStarted;
        internal static int SubscriberCount => OnRaidStarted?.GetInvocationList().Length ?? 0;
        internal static void RaiseStarted(EFT.GameWorld world) => OnRaidStarted?.Invoke(world);
    }
}
namespace ArenaMode.Session
{
    internal static class ArenaSession
    {
        internal static EFT.LocalGame ActiveGame;
        internal static EFT.GameWorld ActiveWorld;
        internal static bool IsExactActiveRaid(EFT.LocalGame game, EFT.GameWorld world)
            => ReferenceEquals(ActiveGame, game) && ReferenceEquals(ActiveWorld, world);
    }
}
namespace Redeploy.Core.Compat
{
    internal sealed class AdapterProbe(string name)
    {
        internal readonly string Name = name;
        internal string Rejection;
        internal bool ThrowPrepare, ThrowTick;
        internal int Checks, Preparations, Aborts, Completions, Ticks;
        internal readonly List<EFT.GameWorld> Retired = new();
        internal string GetRejectionReason() { Checks++; Probes.Trace.Add(Name + ".check"); return Rejection; }
        internal void Prepare(EFT.LocalGame game, EFT.LocalPlayer body)
        {
            Preparations++;
            Probes.Trace.Add(Name + ".prepare");
            if (!BodyReplacementContext.IsCreating || !BodyReplacementContext.OwnsBody(body)
                || !ReferenceEquals(BodyReplacementContext.CurrentGame, game))
                throw new InvalidOperationException("Adapter called before production context adoption");
            if (ThrowPrepare) throw new InvalidOperationException(Name + " prepare fault");
        }
        internal void Abort() { Aborts++; Probes.Trace.Add(Name + ".abort"); }
        internal void Complete() { Completions++; Probes.Trace.Add(Name + ".complete"); }
        internal void Tick() { Ticks++; if (ThrowTick) throw new InvalidOperationException(Name + " tick fault"); }
        internal void Reset()
        {
            Rejection = null;
            ThrowPrepare = ThrowTick = false;
            Checks = Preparations = Aborts = Completions = Ticks = 0;
            Retired.Clear();
        }
    }
    internal static class Probes
    {
        internal static readonly AdapterProbe Recoil = new("recoil"), Ammo = new("ammo"), Healing = new("healing"), Fov = new("fov"), Manimal = new("manimal"), MagazineCheck = new("magazineCheck"), Crosshair = new("crosshair"), Radar = new("radar");
        internal static readonly AdapterProbe[] All = [Recoil, Ammo, Healing, Fov, Manimal, MagazineCheck, Crosshair, Radar];
        internal static readonly List<string> Trace = new();
        internal static void Reset() { foreach (var probe in All) probe.Reset(); Trace.Clear(); }
    }
    internal static class RecoilLegacyContract { internal const string PluginGuid = "fixture.recoil"; }
    internal static class ContinuousLoadAmmoContract { internal const string PluginGuid = "fixture.ammo"; }
    internal static class ContinuousHealingContract { internal const string PluginGuid = "fixture.healing"; }
    internal static class FovFixContract { internal const string PluginGuid = "fixture.fov"; }
    internal static class ManimalAmmoContract { internal const string PluginGuid = "fixture.manimal"; }
    internal static class MagazineCheckContract { internal const string PluginGuid = "fixture.magazineCheck"; }
    internal static class SimpleCrosshairContract { internal const string PluginGuid = "fixture.crosshair"; }
    internal static class RadarContract { internal const string PluginGuid = "fixture.radar"; }
    internal static class SimpleCrosshairCompat
    {
        internal static string GetRejectionReason() => Probes.Crosshair.GetRejectionReason();
        internal static void Prepare(EFT.LocalGame game, EFT.LocalPlayer body) => Probes.Crosshair.Prepare(game, body);
        internal static void AbortPreparation() => Probes.Crosshair.Abort();
        internal static void CompletePreparation() => Probes.Crosshair.Complete();
        internal static void Tick() => Probes.Crosshair.Tick();
        internal static void RetireWorld(EFT.GameWorld world) => Probes.Crosshair.Retired.Add(world);
    }
    internal static class RadarCompat
    {
        internal static string GetRejectionReason() => Probes.Radar.GetRejectionReason();
        internal static void Prepare(EFT.LocalGame game, EFT.LocalPlayer body) => Probes.Radar.Prepare(game, body);
        internal static void AbortPreparation() => Probes.Radar.Abort();
        internal static void Tick() => Probes.Radar.Tick();
        internal static void RetireWorld(EFT.GameWorld world) => Probes.Radar.Retired.Add(world);
    }
    internal static class MagazineCheckCompat
    {
        internal static string GetRejectionReason() => Probes.MagazineCheck.GetRejectionReason();
        internal static void Prepare(EFT.LocalGame game, EFT.LocalPlayer body) => Probes.MagazineCheck.Prepare(game, body);
        internal static void AbortPreparation() => Probes.MagazineCheck.Abort();
        internal static void CompletePreparation() => Probes.MagazineCheck.Complete();
        internal static void Tick() => Probes.MagazineCheck.Tick();
        internal static void RetireWorld(EFT.GameWorld world) => Probes.MagazineCheck.Retired.Add(world);
    }
    internal static class FovFixCompat
    {
        internal static string GetRejectionReason() => Probes.Fov.GetRejectionReason();
        internal static void Prepare(EFT.LocalGame game, EFT.LocalPlayer body) => Probes.Fov.Prepare(game, body);
        internal static void AbortPreparation() => Probes.Fov.Abort();
        internal static void CompletePreparation() => Probes.Fov.Complete();
        internal static void Tick() => Probes.Fov.Tick();
        internal static void RetireWorld(EFT.GameWorld world) => Probes.Fov.Retired.Add(world);
    }
    internal static class ManimalAmmoCompat
    {
        internal static string GetRejectionReason() => Probes.Manimal.GetRejectionReason();
        internal static void Prepare(EFT.LocalGame game, EFT.LocalPlayer body) => Probes.Manimal.Prepare(game, body);
        internal static void AbortPreparation() => Probes.Manimal.Abort();
        internal static void Tick() => Probes.Manimal.Tick();
        internal static void RetireWorld(EFT.GameWorld world) => Probes.Manimal.Retired.Add(world);
    }
    internal static class RecoilLegacyCompat
    {
        internal static string GetRejectionReason() => Probes.Recoil.GetRejectionReason();
        internal static void Prepare(EFT.LocalGame game, EFT.LocalPlayer body) => Probes.Recoil.Prepare(game, body);
        internal static void AbortPreparation() => Probes.Recoil.Abort();
        internal static void CompletePreparation() => Probes.Recoil.Complete();
        internal static void Tick() => Probes.Recoil.Tick();
        internal static void RetireWorld(EFT.GameWorld world) => Probes.Recoil.Retired.Add(world);
    }
    internal static class ContinuousLoadAmmoCompat
    {
        internal static string GetRejectionReason() => Probes.Ammo.GetRejectionReason();
        internal static void Prepare(EFT.LocalGame game, EFT.LocalPlayer body) => Probes.Ammo.Prepare(game, body);
        internal static void AbortPreparation() => Probes.Ammo.Abort();
        internal static void Tick() => Probes.Ammo.Tick();
    }
    internal static class ContinuousHealingCompat
    {
        internal static string GetRejectionReason() => Probes.Healing.GetRejectionReason();
        internal static void Prepare(EFT.LocalGame game, EFT.LocalPlayer body) => Probes.Healing.Prepare(game, body);
        internal static void Tick() => Probes.Healing.Tick();
    }
}
