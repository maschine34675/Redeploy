using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)] public class BepInPlugin : Attribute
    { public string GUID; public Version Version; public BepInPlugin(string guid, string name, string version) { GUID = guid; Version = new(version); } }
    public class BaseUnityPlugin { }
    public class PluginInfo { public object Instance; public BepInPlugin Metadata; }
}
namespace BepInEx.Bootstrap
{ public static class Chainloader { public static Dictionary<string, BepInEx.PluginInfo> PluginInfos = new(); } }
namespace Comfort.Common
{ public static class Singleton<T> where T : class { public static T Instance; public static bool Instantiated => Instance != null; } }
namespace UnityEngine
{ public struct Vector2 { public float x, y; public Vector2(float a, float b) { x = a; y = b; } } public class GameObject { public bool activeSelf; } }
namespace EFT.HealthSystem
{ public interface IHealthController { bool IsAlive { get; } } public sealed class Health : IHealthController { public bool IsAlive { get; set; } = true; } }
namespace EFT.UI
{
    public class UIParent { public Action _oneCase; public List<Action> _destroys = new(); }
    public class UIElement
    {
        protected readonly UIParent UI = new();
        public UnityEngine.GameObject GameObject = new();
        public bool ThrowAfterDispose;
        public int Disposals; public void AddDisposable(Action action) => UI._destroys.Add(action);
        public void HideGameObject() => GameObject.activeSelf = false;
        public void ShowGameObject() => GameObject.activeSelf = true;
        public void Dispose() { Disposals++; UI._oneCase?.Invoke(); UI._oneCase = null; foreach (var action in UI._destroys.ToArray()) action(); UI._destroys.Clear(); HideGameObject(); if (ThrowAfterDispose) { ThrowAfterDispose = false; throw new InvalidOperationException("fixture partial dispose"); } }
    }
}
namespace EFT
{
    public class Profile { public Inventory Inventory = new(); }
    public class Inventory { public event Action Weight; public int Subscribers => Weight?.GetInvocationList().Length ?? 0; public float Value; public void Set(float value) { Value = value; Weight?.Invoke(); } public void Bind(Action callback) => Weight += callback; public void Unbind(Action callback) => Weight -= callback; }
    public enum GameStatus { Running, Starting, Started, Stopped }
    public class AbstractGame { }
    public class LocalGame : AbstractGame { public GameWorld GameWorld; public Profile Profile; public LocalPlayer LocalPlayer; public Owner PlayerOwner = new(); public GameStatus Status = GameStatus.Running; }
    public class Owner { public Player Player; }
    public class GameWorld { public Player MainPlayer; }
    public class HideoutGameWorld : GameWorld { }
    public class PhysicalBase { public UnityEngine.Vector2 BaseOverweightLimits, WalkOverweightLimits; }
    public class Player { public GameWorld GameWorld; public Profile Profile; public bool IsYourPlayer, IsAI; public HealthSystem.IHealthController HealthController = new HealthSystem.Health(); public PhysicalBase Physical = new(); }
    public class LocalPlayer : Player { }
}
namespace Redeploy.Core { internal static class CoreLog { public static void LogWarning(string text) { } } }
namespace PlayerEncumbranceBar
{
    [BepInEx.BepInPlugin("com.mpstark.PlayerEncumbranceBar", "PlayerEncumbranceBar", "1.3.0")]
    public class Plugin : BepInEx.BaseUnityPlugin { public PlayerEncumbranceBarComponent PlayerEncumbranceBar = new(); }
    public class PlayerEncumbranceBarComponent : EFT.UI.UIElement
    {
        private static EFT.HealthSystem.IHealthController _healthController;
        private UnityEngine.Vector2 _baseOverweightLimits, _walkOverweightLimits;
        public float DisplayedWeight; public int Updates, Shows; public float DisplayedBase, DisplayedWalk;
        public bool ThrowOnShow;
        public static object Health => _healthController;
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void Show(EFT.HealthSystem.IHealthController health)
        {
            Shows++; if (ThrowOnShow) { ThrowOnShow = false; throw new InvalidOperationException("fixture show failed"); }
            Dispose(); _healthController = health;
            var inventory = Utils.GameUtils.PlayerProfile.Inventory;
            Action callback = () => UpdateWeight(false);
            inventory.Bind(callback); AddDisposable(() => inventory.Unbind(callback));
            UpdateWeightLimits(false);
            GameObject.activeSelf = true;
        }
        [MethodImpl(MethodImplOptions.NoInlining)] private void UpdateWeight(bool tween) { Updates++; DisplayedWeight = Utils.GameUtils.PlayerProfile.Inventory.Value; }
        [MethodImpl(MethodImplOptions.NoInlining)] private void UpdateWeightLimits(bool tween)
        { _baseOverweightLimits = new(20, 40); _walkOverweightLimits = new(30, 50); DisplayedBase = _baseOverweightLimits.x; DisplayedWalk = _walkOverweightLimits.x; UpdateWeight(tween); }
        [MethodImpl(MethodImplOptions.NoInlining)] public void Refresh() => UpdateWeightLimits(false);
    }
}
namespace PlayerEncumbranceBar.Utils
{
    public static class GameUtils
    { public static EFT.Profile Backend = new(); public static EFT.Profile PlayerProfile { [MethodImpl(MethodImplOptions.NoInlining)] get => Backend; } }
}
