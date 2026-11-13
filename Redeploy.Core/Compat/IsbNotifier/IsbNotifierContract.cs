using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;

namespace Redeploy.Core.Compat;

/// <summary>The private presentation latch in the independently registered notifier plugin.</summary>
internal sealed class IsbNotifierContract
{
    internal const string PluginGuid = "com.samc137.ISBinfo";
    internal static readonly Version Reviewed = new(2, 0, 4, 0);
    internal readonly FieldInfo Ended, Instance;
    internal readonly MethodInfo MarkEnded, Reset;
    private readonly MethodInfo _enabled, _headless;

    internal IsbNotifierContract(Type plugin, string guid, Version reported)
    {
        var version = Normalize(reported);
        var metadata = plugin?.GetCustomAttribute<BepInPlugin>(false);
        if (version == null || version < Reviewed)
            throw new NotSupportedException("ISB SOF Notifier 2.0.4 or newer is required");
        if (guid != PluginGuid || plugin?.FullName != "ISBInfo.ISBInfoPlugin"
            || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin) || plugin.Assembly.GetName().Name != "ISBNotify"
            || metadata?.GUID != guid || Normalize(metadata.Version) != version
            || Normalize(plugin.Assembly.GetName().Version) != version)
            throw new InvalidOperationException("ISB SOF Notifier plugin/assembly identity changed");
        Ended = Field(plugin, "LocalRaidPresentationEnded", typeof(bool));
        Instance = Field(plugin, "Instance", plugin);
        Field(plugin, "EnablePlugin", typeof(ConfigEntry<bool>));
        Field(plugin, "LocalPlayerObservedInRaid", typeof(bool));
        Field(plugin, "NextPresentationStateRefreshTime", typeof(float));
        Method(plugin, "RefreshLocalRaidPresentationState", typeof(void));
        Method(plugin, "CanPresentLocally", typeof(bool));
        _enabled = Method(plugin, "IsPluginEnabled", typeof(bool));
        _headless = Method(plugin, "IsHeadless", typeof(bool));
        Reset = Method(plugin, "ResetState", typeof(void));
        MarkEnded = Method(plugin, "MarkLocalRaidPresentationEnded", typeof(void), isPublic: true);
    }

    internal void VerifyInstance(object plugin)
    {
        if (!ReferenceEquals(Instance.GetValue(null), plugin))
            throw new InvalidOperationException("ISB SOF Notifier singleton changed");
    }

    internal bool PresentationEnabled => (bool)_enabled.Invoke(null, null) && !(bool)_headless.Invoke(null, null);
    internal static Version Normalize(Version version) => version == null ? null
        : new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));

    private static FieldInfo Field(Type type, string name, Type expected)
    {
        var field = type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (field == null || !field.IsPrivate || field.FieldType != expected || field.IsInitOnly || field.IsLiteral)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private static MethodInfo Method(Type type, string name, Type result, bool isPublic = false)
    {
        var method = type.GetMethod(name, BindingFlags.Static | BindingFlags.DeclaredOnly
            | (isPublic ? BindingFlags.Public : BindingFlags.NonPublic), null, Type.EmptyTypes, null);
        if (method == null || method.ReturnType != result || method.IsGenericMethod
            || (isPublic ? !method.IsPublic : !method.IsPrivate))
            throw new MissingMethodException(type.FullName, name);
        return method;
    }
}
