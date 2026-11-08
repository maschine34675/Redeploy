using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using EFT;
using HarmonyLib;

namespace Redeploy.Core.Compat;

internal static class AmandsSenseCompat
{
    internal const string PluginGuid = "xyz.drakia.Sense";
    private static readonly System.Version Reviewed = new(3, 1, 0, 0);
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.AmandsSenseCompat");
    private static readonly ConditionalWeakTable<Player, object> Discarded = new();
    private static BaseUnityPlugin _plugin;
    private static FieldInfo _player, _inventory;
    private static Action<GameWorld> _prune;
    private static Player _candidate;
    private static GameWorld _world;
    private static bool _skipSoundReload;
    private static bool _patched;
    private static string _warning;

    internal static string GetRejectionReason()
    {
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info)) return null;
        if (info?.Instance == null) return "Amand's Sense has not finished loading.";
        if (ReferenceEquals(_plugin, info.Instance)) return _warning;
        try
        {
            var assembly = StandaloneHudContract.Plugin(info.Instance.GetType(), info.Metadata?.GUID, PluginGuid,
                info.Metadata?.Version, Reviewed, "AmandsSense.AmandsSensePlugin", "AmandsSense", Reviewed);
            var sense = assembly.GetType("AmandsSense.Components.AmandsSenseClass", true);
            var record = assembly.GetType("AmandsSense.Models.SenseDeadPlayer", true);
            var hook = StandaloneHudContract.Method(assembly.GetType("AmandsSense.Patches.AmandsPlayerPatch", true),
                "PatchPostFix", typeof(void), true, typeof(Player).MakeByRefType());
            if (Harmony.GetPatchInfo(AccessTools.Method(typeof(Player), nameof(Player.Init)))?.Postfixes.Any(p => p.PatchMethod == hook) != true)
                return "Amand's Sense has not installed its local-player hook.";
            _player = StandaloneHudContract.Field(sense, "Player", typeof(Player), true);
            _inventory = StandaloneHudContract.Field(sense, "inventoryControllerClass", typeof(Player.PlayerInventoryController), true);
            var records = StandaloneHudContract.Field(sense, "DeadPlayers", typeof(List<>).MakeGenericType(record), true);
            var victim = StandaloneHudContract.Field(record, "victim", typeof(Player));
            _prune = (Action<GameWorld>)AccessTools.Method(typeof(AmandsSenseCompat), nameof(CreatePruner))
                .MakeGenericMethod(record).Invoke(null, new object[] { records, victim });
            _patched = true;
            Patch(sense, "Clear", true, nameof(BeforeClear));
            Patch(sense, "ReloadFiles", true, nameof(BeforeReload), typeof(bool));
            Patch(sense, "SenseDeadBodies", false, nameof(BeforeSenseDeadBodies));
            _plugin = info.Instance;
            if (StandaloneHudContract.Normalize(info.Metadata.Version) > Reviewed)
                CoreLog.LogWarning("Amand's Sense " + info.Metadata.Version + " is newer than reviewed 3.1.0; its compatibility bindings matched.");
            return _warning = null;
        }
        catch (Exception ex)
        {
            if (_patched) { Patches.UnpatchSelf(); _patched = false; }
            _plugin = info.Instance; // Structural drift stays the same across menu polls.
            var message = "Core cannot safely replace the player with Amand's Sense: " + ex.GetBaseException().Message;
            if (_warning != message) CoreLog.LogWarning(message);
            return _warning = message;
        }
    }

    private static void Patch(Type sense, string method, bool isStatic, string prefix, params Type[] parameters)
    {
        var target = StandaloneHudContract.Method(sense, method, typeof(void), isStatic, parameters);
        Patches.Patch(target, prefix: new HarmonyMethod(typeof(AmandsSenseCompat), prefix) { priority = Priority.First });
    }

    private static bool BeforeClear()
    {
        // Sense's native Player.Init postfix already assigned the player and inventory. Its
        // subsequent Clear removes markers while enumerating them and erases same-raid kills.
        if (!BodyReplacementContext.IsCreating || _player.GetValue(null) is not LocalPlayer body
            || !BodyReplacementContext.TryOwnCandidate(body)) return true;
        _candidate = body;
        _world = body.GameWorld;
        _skipSoundReload = true;
        return false;
    }

    private static bool BeforeReload(bool __0)
    {
        if (!__0 || !_skipSoundReload) return true;
        _skipSoundReload = false;
        return false; // The existing world already loaded these clips.
    }

    internal static void EndAttempt()
    {
        var candidate = _candidate;
        _candidate = null;
        _skipSoundReload = false;
        if (ReferenceEquals(candidate, null) || !BodyReplacementContext.OwnsRaid(BodyReplacementContext.CurrentGame, _world)) return;
        var old = BodyReplacementContext.PreviousBody;
        // LocalGame.LocalPlayer is Core's commit boundary, including a failure during rewire.
        if (ReferenceEquals(BodyReplacementContext.CurrentGame.LocalPlayer, old) && old != null)
        {
            Discarded.GetValue(candidate, _ => new object());
            if (ReferenceEquals(_player.GetValue(null), candidate))
            {
                _player.SetValue(null, old);
                _inventory.SetValue(null, old.InventoryController as Player.PlayerInventoryController);
            }
        }
    }

    private static Action<GameWorld> CreatePruner<T>(FieldInfo records, FieldInfo victim)
    {
        var list = Expression.Lambda<Func<List<T>>>(Expression.Field(null, records)).Compile();
        var row = Expression.Parameter(typeof(T));
        var getVictim = Expression.Lambda<Func<T, Player>>(Expression.Field(row, victim), row).Compile();
        return world => list()?.RemoveAll(entry =>
        {
            var body = getVictim(entry);
            return body == null || !ReferenceEquals(body.GameWorld, world) || Discarded.TryGetValue(body, out _);
        });
    }

    private static void BeforeSenseDeadBodies()
    {
        // Only run alongside a requested body scan in a world where Core created a body.
        if (_world != null && BodyReplacementContext.OwnsRaid(BodyReplacementContext.CurrentGame, _world))
            _prune(_world);
    }

    internal static void RetireWorld(GameWorld world)
    {
        if (!ReferenceEquals(world, _world)) return;
        _world = null;
        _candidate = null;
        _skipSoundReload = false;
    }
}
