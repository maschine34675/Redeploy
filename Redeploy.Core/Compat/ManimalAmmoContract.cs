using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BepInEx;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

internal sealed class ManimalAmmoContract
{
    internal const string PluginGuid = "com.manimal.loadammoanim";
    internal static readonly Version ReviewedVersion = new(2, 0, 0, 0);
    internal sealed class Iterator
    {
        internal MethodInfo Factory, MoveNext;
        internal FieldInfo Player;
    }
    internal Type SessionType, ControllerType;
    internal FieldInfo Sessions, LoadingCount, Animating, ActiveController, Loop, Magazine, Renderer;
    internal FieldInfo DrawPending, MagazineTemplate;
    internal MethodInfo Get, TryGet, Ended, TryStart, SpawnController, DetectPrefix, DetectPostfix, DelayPrefix;
    internal MethodInfo NativeStart, NativeDelay, Register;
    internal MethodInfo[] BodyActions;
    internal Iterator[] Iterators;

    internal static ManimalAmmoContract Resolve(Type plugin, string guid, Version reported, out Version version)
    {
        version = Normalize(reported);
        if (guid != PluginGuid || version == null || version < ReviewedVersion)
            throw new NotSupportedException("Manimal Ammo Loading Animations 2.0.0 or newer is required");
        if (plugin?.FullName != "Manimal.LoadAmmoAnim.Plugin" || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin) || plugin.IsAbstract)
            throw new MissingMemberException("Manimal plugin identity changed");
        var assembly = plugin.Assembly; var attr = plugin.GetCustomAttribute<BepInPlugin>(false);
        if (assembly.GetName().Name != "LoadAmmoAnimClient" || Normalize(assembly.GetName().Version) != version
            || attr?.GUID != PluginGuid || Normalize(attr.Version) != version)
            throw new InvalidOperationException("Manimal plugin and assembly metadata disagree");
        var c = new ManimalAmmoContract();
        var state = assembly.GetType("Manimal.LoadAmmoAnim.Patches.LoadAmmoAnimState", true);
        var driver = assembly.GetType("Manimal.LoadAmmoAnim.Patches.LoadAmmoAnimDriver", true);
        c.SessionType = assembly.GetType("Manimal.LoadAmmoAnim.Patches.PlayerSession", true);
        c.ControllerType = assembly.GetType("Manimal.LoadAmmoAnim.CustomEFTData.LoadAmmoBundleController", true);
        var item = assembly.GetType("Manimal.LoadAmmoAnim.CustomEFTData.LoadAmmoBundleItem", true);
        if (!typeof(Player.UsableItemController).IsAssignableFrom(c.ControllerType)
            || !typeof(Item).IsAssignableFrom(item) || !c.SessionType.IsSealed)
            throw new MissingMemberException("Manimal controller/item/session shape changed");
        c.Sessions = Field(state, "_sessions", true, typeof(Dictionary<,>).MakeGenericType(typeof(Player), c.SessionType), writable: false);
        c.LoadingCount = Field(c.SessionType, "LoadingCount", false, typeof(int));
        c.Animating = Field(c.SessionType, "IsOurAnimation", false, typeof(bool));
        c.ActiveController = Field(c.SessionType, "ActiveController", false, c.ControllerType);
        c.Loop = Field(c.SessionType, "LoopCoroutine", false, typeof(Coroutine));
        c.Magazine = Field(c.SessionType, "CurrentMag", false, typeof(Magazine));
        c.Renderer = Field(c.SessionType, "ActiveRenderer", false, typeof(MeshRenderer));
        c.DrawPending = Field(c.SessionType, "DrawPhasePending", false, typeof(bool));
        c.MagazineTemplate = Field(c.SessionType, "CurrentMagTemplateId", false, typeof(string));
        c.Get = Method(state, "Get", true, c.SessionType, typeof(Player));
        c.TryGet = Method(state, "TryGet", true, c.SessionType, typeof(Player));
        c.Ended = Method(state, "OnLoadingEnded", true, typeof(void), typeof(Player));
        c.TryStart = Method(driver, "TryStart", true, typeof(void), typeof(Player), item);
        c.SpawnController = Method(driver, "CreateAndSpawnBundleController", true, typeof(void), typeof(Player), item);
        c.BodyActions = new[] {
            Method(state, "OnLoadingStarted", true, typeof(void), typeof(Player)),
            Method(state, "ForceResetLoading", true, typeof(void), typeof(Player)),
            Method(driver, "ApplyMeshSelection", true, typeof(void), typeof(Player)),
            Method(driver, "StopAnimationInstantly", true, typeof(void), typeof(Player)),
            Method(driver, "StartBundleAnim", true, typeof(void), typeof(Player), typeof(string), typeof(float)),
            Method(driver, "StopBundleAnim", true, typeof(void), typeof(Player), typeof(bool)),
            Method(driver, "SwapMagMesh", true, typeof(void), typeof(Player), typeof(string))
        };
        c.Iterators = new[] {
            IteratorMethod(driver, "StartNextFrame", typeof(Player)),
            IteratorMethod(driver, "PlayPutawayThenRestore", typeof(Player), c.ControllerType),
            IteratorMethod(driver, "SwapMagMeshCoroutine", typeof(Player), c.ControllerType),
            IteratorMethod(driver, "AnimLoop", typeof(Player), c.ControllerType)
        };
        var detect = assembly.GetType("Manimal.LoadAmmoAnim.Patches.LoadAmmoAnimDetectPatch", true);
        var delay = assembly.GetType("Manimal.LoadAmmoAnim.Patches.LoadMagazineDrawDelayPatch", true);
        var process = typeof(Player.PlayerInventoryController.LoadMagazineProcess);
        c.DetectPrefix = Method(detect, "Prefix", true, typeof(void), process);
        c.DetectPostfix = Method(detect, "Postfix", true, typeof(void), typeof(Task<IResult>));
        c.DelayPrefix = Method(delay, "Prefix", true, typeof(bool), process, typeof(Task).MakeByRefType());
        c.NativeStart = Method(process, "Start", false, typeof(Task<IResult>));
        c.NativeDelay = Method(process, "method_5", false, typeof(Task));
        c.Register = Method(typeof(GameWorld), nameof(GameWorld.RegisterPlayer), false, typeof(void), typeof(IPlayer));
        c.VerifyNativeBindings();
        return c;
    }

    internal void VerifyNativeBindings()
    {
        Receipt(NativeStart, DetectPrefix, true); Receipt(NativeStart, DetectPostfix, false);
        Receipt(NativeDelay, DelayPrefix, true);
        if (Sessions.GetValue(null) is not IDictionary dictionary || dictionary.IsReadOnly || dictionary.IsFixedSize)
            throw new InvalidOperationException("Manimal session registry is not a mutable dictionary");
    }
    internal IDictionary Registry => (IDictionary)Sessions.GetValue(null);
    internal object FindSession(object body)
    {
        if (body == null) return null;
        foreach (DictionaryEntry entry in Registry)
            if (ReferenceEquals(entry.Key, body)) return entry.Value;
        return null;
    }
    private static Iterator IteratorMethod(Type driver, string name, params Type[] args)
    {
        var factory = Method(driver, name, true, typeof(IEnumerator), args);
        var type = factory.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
        if (type == null || type.Assembly != driver.Assembly || type.DeclaringType != driver || !typeof(IEnumerator).IsAssignableFrom(type))
            throw new MissingMemberException("Manimal iterator shape changed: " + name);
        return new Iterator { Factory = factory, MoveNext = Method(type, "MoveNext", false, typeof(bool)),
            Player = Field(type, "player", false, typeof(Player)) };
    }
    private static void Receipt(MethodInfo target, MethodInfo foreign, bool prefix)
    {
        var info = Harmony.GetPatchInfo(target);
        var patches = prefix ? info?.Prefixes : info?.Postfixes;
        if (patches == null || patches.Count(p => p.PatchMethod == foreign) != 1)
            throw new MissingMethodException("Manimal native animation hook changed: " + foreign.Name);
    }
    private static MethodInfo Method(Type type, string name, bool stat, Type result, params Type[] args)
    {
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, args, null);
        if (method == null || method.IsStatic != stat || method.ReturnType != result || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }
    private static FieldInfo Field(Type type, string name, bool stat, Type value, bool writable = true)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        if (field == null || field.IsStatic != stat || field.FieldType != value || (writable && field.IsInitOnly))
            throw new MissingFieldException(type.FullName, name);
        return field;
    }
    internal static Version Normalize(Version value) => value == null || value.Build < 0 ? null
        : new Version(value.Major, value.Minor, value.Build, Math.Max(value.Revision, 0));
}
