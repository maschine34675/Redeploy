using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)] public class BepInPlugin : Attribute
    { public string GUID { get; } public Version Version { get; } public BepInPlugin(string guid, string name, string version) { GUID = guid; Version = new Version(version); } }
    public class BaseUnityPlugin : MonoBehaviour { }
    public class PluginInfo { public BaseUnityPlugin Instance; public BepInPlugin Metadata; }
}
namespace BepInEx.Bootstrap { public static class Chainloader { public static readonly Dictionary<string, PluginInfo> PluginInfos = new(); } }
namespace Comfort.Common { public static class Singleton<T> { public static T Instance; public static bool Instantiated => Instance != null; } }
namespace Redeploy.Core { public static class CoreLog { public static int Warnings; public static void LogWarning(string message) { Warnings++; Console.WriteLine(message); } public static void LogDebug(string message) { } } }
namespace Redeploy.Core.Api
{
    public static class RespawnApi { public static event Action<Player, Player, string> Replaced; public static void Publish(Player old, Player next) => Replaced?.Invoke(old, next, "death"); }
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
        public override int GetHashCode() => Destroyed ? 0 : RuntimeHelpers.GetHashCode(this);
    }
    public class Component : Object { public GameObject gameObject; public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>(); }
    public class MonoBehaviour : Component { public bool enabled = true; }
    public class GameObject : Object
    {
        public string name; public bool activeInHierarchy = true;
        private readonly Dictionary<Type, Component> _components = new();
        public T Add<T>() where T : Component, new() { var c = new T { gameObject = this }; _components[typeof(T)] = c; return c; }
        public T GetComponent<T>() where T : Component => _components.TryGetValue(typeof(T), out var value) ? (T)value : null;
    }
    public class Camera : MonoBehaviour { public static Camera main; }
    public class Light : Component { public float range; }
    public struct Color { public float r; }
    public struct GradientColorKey { public Color color; }
    public class AnimationCurve { }
}
namespace UnityEngine.SceneManagement { public struct Scene { public string name; } public static class SceneManager { public static string Name = "Factory_Rework_Day_Scripts"; public static Scene GetActiveScene() => new() { name = Name }; } }
public class PrismEffects : MonoBehaviour { [MethodImpl(MethodImplOptions.NoInlining)] public void OnEnable() { } }
public class VolumetricLight : Component { public float ExtinctionCoef; }
public class LevelSettings : MonoBehaviour { }
namespace EFT
{
    public class HealthController { public bool IsAlive = true; }
    public class Player : MonoBehaviour
    {
        public bool IsYourPlayer = true; public GameWorld GameWorld; public object Profile = new(); public HealthController HealthController = new();
        [MethodImpl(MethodImplOptions.NoInlining)] public void Init() { }
    }
    public class LocalPlayer : Player { }
    public class GameWorld { public Player MainPlayer; public List<Player> RegisteredPlayers = new(); }
    public class HideoutGameWorld : GameWorld { }
    public class AbstractGame { }
    public enum GameStatus { Started, Starting, Running, Stopped }
    public class LocalGame : AbstractGame { public Player LocalPlayer; public GameWorld GameWorld; public object Profile; public GameStatus Status = GameStatus.Started; }
}
namespace EFT.CameraControl
{
    public class CameraManager { public static CameraManager Instance; public Camera Camera; }
    public class PlayerCameraController { public Player Player; }
}

// Deliberately small synthetic implementation. It models only the reviewed native
// activation boundary, retained world defaults and light registries; no mod code is copied.
public static class Fixture
{
    public static Type GraphicsType;
    public static object Graphics;
    public static int Activations, Updates, Resets;
    public static float WorldLighting = 20;
    public static bool ThrowActivation;
    public static T Get<T>(string name) => (T)GraphicsType.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance).GetValue(name == "GraphicsMode" ? Graphics : null);
    public static void Set(string name, object value) => GraphicsType.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance).SetValue(name == "GraphicsMode" ? Graphics : null, value);
    public static void Call(string name, params object[] args) => GraphicsType.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Graphics, args);
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Activate(object self, GameObject camera, PrismEffects prism)
    {
        Activations++;
        if (Get<GameObject>("FPSCamera") != null) return;
        Set("FPSCamera", camera); Set("FPSCameraCamera", camera.GetComponent<Camera>()); Set("FPSCameraPrismEffects", prism);
        Set("defaultZeroLevel", WorldLighting); Set("GraphicsMode", true);
        Get<Dictionary<Light, float>>("registeredLights").Clear(); Get<Dictionary<VolumetricLight, float>>("registeredVolumetricLights").Clear();
        if (ThrowActivation) throw new InvalidOperationException("synthetic native failure");
        Call("UpdateAmandsGraphics");
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Update(object self)
    {
        Updates++; WorldLighting = Get<float>("defaultZeroLevel") + 10;
        foreach (var entry in Get<Dictionary<Light, float>>("registeredLights")) { if (entry.Key == null) throw new Exception("destroyed lamp"); entry.Key.range = entry.Value * 2; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Reset(object self)
    {
        Resets++; WorldLighting = Get<float>("defaultZeroLevel");
        foreach (var entry in Get<Dictionary<Light, float>>("registeredLights")) { if (entry.Key == null) throw new Exception("destroyed lamp"); entry.Key.range = entry.Value; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void PrismEnabled(ref PrismEffects prism)
    {
        if (prism.gameObject.name != "FPS Camera") return;
        Set("GraphicsMode", false);
        Call("ActivateAmandsGraphics", prism.gameObject, prism);
    }
    public static void Build(string mode)
    {
        var version = mode == "older" ? "1.7.0.0" : mode == "newer" ? "1.8.1.0" : "1.8.0.0";
        var asm = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("AmandsGraphics") { Version = new Version(version) }, AssemblyBuilderAccess.Run);
        var module = asm.DefineDynamicModule("Fixture");
        var graphics = module.DefineType("AmandsGraphics.AmandsGraphicsClass", TypeAttributes.Public, typeof(MonoBehaviour));
        void Field(string name, Type type, bool stat = true) => graphics.DefineField(mode == "drift" && name == "FPSCamera" ? "changedCamera" : name, type, FieldAttributes.Public | (stat ? FieldAttributes.Static : 0));
        Field("Player", typeof(Player)); Field("FPSCamera", typeof(GameObject)); Field("FPSCameraCamera", typeof(Camera)); Field("FPSCameraPrismEffects", typeof(PrismEffects));
        Field("GraphicsMode", typeof(bool), false); Field("levelSettings", typeof(LevelSettings)); Field("scene", typeof(string));
        Field("registeredLights", typeof(Dictionary<Light, float>)); Field("registeredVolumetricLights", typeof(Dictionary<VolumetricLight, float>));
        Field("defaultLightsUseLinearIntensity", typeof(bool));
        foreach (var name in new[] { "defaultZeroLevel", "defaultLightIntensity", "defaultSunMeshBrightness", "defaultScatteringBrightnessMultiplier" }) Field(name, typeof(float));
        foreach (var name in new[] { "defaultSkyColor", "defaultEquatorColor", "defaultGroundColor", "defaultNightVisionSkyColor", "defaultNightVisionEquatorColor", "defaultNightVisionGroundColor" }) Field(name, typeof(Color));
        Field("defaultGradientColorKeys", typeof(GradientColorKey[])); Field("defaultAmbientBrightness", typeof(AnimationCurve)); Field("defaultAmbientContrast", typeof(AnimationCurve));
        void Forward(string name, string target, params Type[] parameters)
        {
            var method = graphics.DefineMethod(name, MethodAttributes.Public, typeof(void), parameters); method.SetImplementationFlags(MethodImplAttributes.NoInlining);
            var il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); for (int i = 0; i < parameters.Length; i++) il.Emit(OpCodes.Ldarg, i + 1);
            il.Emit(OpCodes.Call, typeof(Fixture).GetMethod(target)); il.Emit(OpCodes.Ret);
        }
        Forward("ActivateAmandsGraphics", nameof(Activate), typeof(GameObject), typeof(PrismEffects)); Forward("UpdateAmandsGraphics", nameof(Update)); Forward("ResetGraphics", nameof(Reset));
        GraphicsType = graphics.CreateType(); Graphics = Activator.CreateInstance(GraphicsType);
        var plugin = module.DefineType("AmandsGraphics.AmandsGraphicsPlugin", TypeAttributes.Public, typeof(BaseUnityPlugin));
        plugin.SetCustomAttribute(new CustomAttributeBuilder(typeof(BepInPlugin).GetConstructor(new[] { typeof(string), typeof(string), typeof(string) }), new object[] { "com.Amanda.Graphics", "Fixture", version }));
        var holder = plugin.DefineField("Holder", GraphicsType, FieldAttributes.Static | FieldAttributes.Public);
        var prop = plugin.DefineProperty("AmandsGraphicsClass", PropertyAttributes.None, GraphicsType, Type.EmptyTypes);
        var getter = plugin.DefineMethod("get_AmandsGraphicsClass", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName, GraphicsType, Type.EmptyTypes);
        var gil = getter.GetILGenerator(); gil.Emit(OpCodes.Ldsfld, holder); gil.Emit(OpCodes.Ret); prop.SetGetMethod(getter);
        var runtime = plugin.CreateType(); runtime.GetField("Holder").SetValue(null, Graphics);
        MethodInfo Hook(string name, Type parameter)
        {
            var type = module.DefineType("AmandsGraphics.Patches." + name, TypeAttributes.Public);
            var method = type.DefineMethod("PatchPostFix", MethodAttributes.Public | MethodAttributes.Static, typeof(void), new[] { parameter.MakeByRefType() });
            method.DefineParameter(1, ParameterAttributes.None, "__instance"); method.SetImplementationFlags(MethodImplAttributes.NoInlining);
            var il = method.GetILGenerator();
            if (parameter == typeof(PrismEffects)) { il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, typeof(Fixture).GetMethod(nameof(PrismEnabled))); }
            il.Emit(OpCodes.Ret);
            return type.CreateType().GetMethod("PatchPostFix");
        }
        var native = new Harmony("fixture.amand.native");
        native.Patch(typeof(Player).GetMethod("Init"), postfix: new HarmonyMethod(Hook("AmandsPlayerPatch", typeof(Player))));
        native.Patch(typeof(PrismEffects).GetMethod("OnEnable"), postfix: new HarmonyMethod(Hook("AmandsGraphicsPrismEffectsPatch", typeof(PrismEffects))));
        Chainloader.PluginInfos["com.Amanda.Graphics"] = new PluginInfo { Instance = (BaseUnityPlugin)Activator.CreateInstance(runtime), Metadata = runtime.GetCustomAttribute<BepInPlugin>() };
        Set("registeredLights", new Dictionary<Light, float>()); Set("registeredVolumetricLights", new Dictionary<VolumetricLight, float>()); Set("GraphicsMode", true); Set("defaultZeroLevel", 10f);
    }
}
