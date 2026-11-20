using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Released 1.2.0 has an auto-generated assembly revision; semantic identity excludes it.</summary>
internal sealed class VisualAssistContract
{
    internal const string PluginGuid = "com.janky.visualassist";
    internal static readonly Version Reviewed = new(1, 2, 0, 0);
    internal Type ArcType;
    internal FieldInfo Player, Line, Sphere, SphereRenderer, Throw, Enabled;
    internal PropertyInfo Instance;
    internal MethodInfo Awake, Update, Destroy, DeadHook, DisposeHook;
    private MethodInfo _start, _startHook, _dead, _dispose;

    internal static VisualAssistContract Resolve(Type plugin, string guid, Version reported, out Version version)
    {
        version = Normalize(reported);
        if (guid != PluginGuid || version == null || version < Reviewed)
            throw new NotSupportedException("VisualAssist 1.2.0 or newer is required");
        if (plugin?.FullName != "VisualAssist.Plugin" || plugin.IsAbstract || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin))
            throw new MissingMemberException("VisualAssist plugin identity changed");
        var assembly = plugin.Assembly;
        var metadata = plugin.GetCustomAttribute<BepInPlugin>(false);
        if (assembly.GetName().Name != "VisualAssist" || Normalize(assembly.GetName().Version) != version
            || metadata?.GUID != guid || Normalize(metadata.Version) != version)
            throw new InvalidOperationException("VisualAssist assembly/plugin metadata disagree");
        var arc = assembly.GetType("VisualAssist.GrenadeArc", true);
        var data = assembly.GetType("VisualAssist.GrenadeThrow", true);
        if (arc.BaseType != typeof(MonoBehaviour) || !data.IsValueType)
            throw new MissingMemberException("VisualAssist component or throw layout changed");
        Field(data, "ThrowPosition", typeof(Vector3)); Field(data, "ThrowForce", typeof(Vector3));
        var c = new VisualAssistContract
        {
            ArcType = arc,
            Player = Field(arc, "localPlayer", typeof(Player)),
            Line = Field(arc, "_line", typeof(LineRenderer)),
            Sphere = Field(arc, "_sphere", typeof(GameObject)),
            SphereRenderer = Field(arc, "_sphereRenderer", typeof(Renderer)),
            Throw = Field(arc, "GrenadeThrow", data),
            Enabled = Field(plugin, "GrenadeArcEnabled", typeof(ConfigEntry<bool>), true),
            Instance = typeof(Singleton<>).MakeGenericType(arc).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static),
            Awake = Method(arc, "Awake", false), Update = Method(arc, "LateUpdate", false), Destroy = Method(arc, "OnDestroy", false),
            DeadHook = Method(assembly.GetType("VisualAssist.GrenadeAssistPlayerOnDeadPrefixPatch", true), "Postfix", true, typeof(Player)),
            DisposeHook = Method(assembly.GetType("VisualAssist.GrenadeAssistPlayerDisposePrefixPatch", true), "Postfix", true, typeof(Player)),
            _start = Method(typeof(GameWorld), "OnGameStarted", false),
            _startHook = Method(assembly.GetType("VisualAssist.GrenadeAssistGameWorldStartedPostfixPatch", true), "Postfix", true, typeof(GameWorld)),
            _dead = typeof(Player).GetMethod(nameof(EFT.Player.OnDead)),
            _dispose = typeof(Player).GetMethod(nameof(EFT.Player.Dispose))
        };
        if (c.Instance?.PropertyType != arc || c.Instance.GetMethod == null || c.Instance.SetMethod == null)
            throw new MissingMemberException("VisualAssist singleton contract changed");
        c.Verify();
        return c;
    }
    internal void Verify()
    {
        if (Harmony.GetPatchInfo(_start)?.Postfixes.Count(p => p.PatchMethod == _startHook) != 1
            || Harmony.GetPatchInfo(_dead)?.Prefixes.Count(p => p.PatchMethod == DeadHook) != 1
            || Harmony.GetPatchInfo(_dispose)?.Prefixes.Count(p => p.PatchMethod == DisposeHook) != 1)
            throw new MissingMethodException("VisualAssist lifecycle hook is missing");
        if (Enabled.GetValue(null) is not ConfigEntry<bool>)
            throw new InvalidOperationException("VisualAssist configuration is not ready");
    }
    private static FieldInfo Field(Type owner, string name, Type type, bool stat = false)
    {
        var field = owner.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (field == null || field.IsStatic != stat || field.IsInitOnly || field.FieldType != type)
            throw new MissingFieldException(owner.FullName, name);
        return field;
    }
    private static MethodInfo Method(Type owner, string name, bool stat, params Type[] args)
    {
        var method = owner.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, args, null);
        if (method == null || method.IsStatic != stat || method.ReturnType != typeof(void)
            || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(owner.FullName, name);
        return method;
    }
    internal static Version Normalize(Version value) => value == null || value.Build < 0 ? null
        : new Version(value.Major, value.Minor, value.Build, 0);
}
