using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace UnityEngine
{
    public class Object { }
    public class Transform : Object
    {
        public Transform parent;
        public bool IsChildOf(Transform other) => ReferenceEquals(this, other) || parent?.IsChildOf(other) == true;
    }
    public class GameObject : Object
    {
        public Transform transform = new();
        public List<object> Children = new();
        public T[] GetComponentsInChildren<T>(bool inactive) => Children.OfType<T>().ToArray();
    }
}
namespace BepInEx
{
    public class BaseUnityPlugin { }
    [AttributeUsage(AttributeTargets.Class)]
    public class BepInPlugin : Attribute
    {
        public string GUID; public Version Version;
        public BepInPlugin(string guid, string name, string version) { GUID = guid; Version = new(version); }
    }
}
namespace BepInEx.Bootstrap
{
    public class PluginInfo { public BepInEx.BaseUnityPlugin Instance; public BepInEx.BepInPlugin Metadata; }
    public static class Chainloader { public static Dictionary<string, PluginInfo> PluginInfos = new(); }
}
namespace Comfort.Common
{
    public static class Singleton<T> where T : class { public static T Instance; public static bool Instantiated => Instance != null; }
}
namespace EFT
{
    public class Profile { }
    public enum GameStatus { Running, Starting, Started, Stopped }
    public class AbstractGame { }
    public class GameWorld { public Player MainPlayer; }
    public class HideoutGameWorld : GameWorld { }
    public class LocalGame : AbstractGame { public GameWorld GameWorld; public LocalPlayer LocalPlayer; public Profile Profile; public GameStatus Status; }
    public class Player : UnityEngine.Object
    {
        public bool IsYourPlayer = true;
        public GameWorld GameWorld;
        public Profile Profile = new();
        public object HandsController;
        public List<OpticSight> Optics = new();
        public Animation ProceduralWeaponAnimation = new();
        public T[] GetComponentsInChildren<T>(bool inactive) => Optics.OfType<T>().ToArray();
        public class FirearmController
        {
            public UnityEngine.GameObject ControllerGameObject = new();
            [MethodImpl(MethodImplOptions.NoInlining)] public void ChangeAimingMode() { }
        }
    }
    public class Animation { public List<object> ScopeAimTransforms = new() { new object() }; public int AimIndex; }
    public class LocalPlayer : Player { }
    public class OpticSight : UnityEngine.Object
    {
        public UnityEngine.Transform transform = new();
        [MethodImpl(MethodImplOptions.NoInlining)] public void OnEnable() { }
        [MethodImpl(MethodImplOptions.NoInlining)] public void OnDisable() { }
    }
}
namespace EFT.CameraControl { }
namespace Redeploy.Core
{
    public static class CoreLog { public static int Warnings; public static void LogWarning(string value) { Warnings++; } }
}
namespace DynamicExternalResolution
{
    [BepInEx.BepInPlugin("com.Shibatsu.DynamicExternalResolution", "DERP fixture", "1.1.2")]
    public class DynamicExternalResolution : BepInEx.BaseUnityPlugin
    {
        private static EFT.Player _localPlayer;
        public static EFT.Player Cached => _localPlayer;
        public static void Cache(EFT.Player body) => _localPlayer = body;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static EFT.Player getPlayerInstance()
        {
            if (_localPlayer != null) return _localPlayer;
            return _localPlayer = Comfort.Common.Singleton<EFT.GameWorld>.Instance.MainPlayer;
        }
    }
    public static class DynamicExternalResolutionPatches
    {
        public static int AimCalls, ResetCalls;
        public static EFT.Player Observed;
        public class OpticSightOnEnablePath
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void PatchPostfix()
            {
                var body = DynamicExternalResolution.getPlayerInstance();
                if (body == null) return;
                _ = body.ProceduralWeaponAnimation.ScopeAimTransforms[0];
                Observed = body; AimCalls++;
            }
        }
        public class OpticSightOnDisablePath
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void PatchPostfix()
            {
                var body = DynamicExternalResolution.getPlayerInstance();
                if (body != null) { Observed = body; ResetCalls++; }
            }
        }
        public class ClientFirearmControllerChangeAimingModePath
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void PatchPostfix() => OpticSightOnEnablePath.PatchPostfix();
        }
        public static void Patch()
        {
            var harmony = new Harmony("fixture.derp");
            var hooks = new[] { typeof(OpticSightOnEnablePath), typeof(OpticSightOnDisablePath), typeof(ClientFirearmControllerChangeAimingModePath) };
            var natives = new[] { typeof(EFT.OpticSight).GetMethod("OnEnable"), typeof(EFT.OpticSight).GetMethod("OnDisable"), typeof(EFT.Player.FirearmController).GetMethod("ChangeAimingMode") };
            for (int i = 0; i < hooks.Length; i++) harmony.Patch(natives[i], postfix: new HarmonyMethod(hooks[i], "PatchPostfix"));
        }
    }
}
