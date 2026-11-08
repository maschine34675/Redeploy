using System;
using System.Reflection;
using BepInEx.Bootstrap;
using EFT;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Finish the outgoing player's climb while it still owns its hands and input.</summary>
internal static class ClimbableLaddersCompat
{
    internal const string PluginGuid = "com.tarkin.ladders";
    private static readonly Version Reviewed = new(1, 0, 4, 0);
    private static object _plugin;
    private static Version _version;
    private static Type _controller;
    private static FieldInfo _player;
    private static string _failure;

    internal static string GetRejectionReason()
    {
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info) || FikaCompatCheck.IsFikaLoaded)
        {
            _plugin = null;
            _version = null;
            _controller = null;
            _player = null;
            _failure = null;
            return null;
        }

        var plugin = info?.Instance;
        if (plugin == null) return "Climbable Ladders has not finished loading.";
        var version = StandaloneHudContract.Normalize(info.Metadata?.Version);
        if (ReferenceEquals(plugin, _plugin) && version == _version) return _failure;

        _plugin = plugin;
        _version = version;
        _controller = null;
        _player = null;
        try
        {
            var assembly = StandaloneHudContract.Plugin(plugin.GetType(), info.Metadata?.GUID, PluginGuid,
                version, Reviewed, "tarkin.ladders.bep.Plugin", "tarkin.ladders.bep", Reviewed);
            var controller = assembly.GetType("tarkin.ladders.bep.PlayerLadderController", true);
            if (!typeof(MonoBehaviour).IsAssignableFrom(controller))
                throw new InvalidOperationException("ladder controller is not a Unity behaviour");
            var player = StandaloneHudContract.Field(controller, "player", typeof(Player));
            StandaloneHudContract.Method(controller, "OnDestroy", typeof(void), false);
            _controller = controller;
            _player = player;
            _failure = null;
            if (version > Reviewed)
                CoreLog.LogWarning($"Climbable Ladders {version} is newer than reviewed 1.0.4; matching APIs, unverified gameplay.");
        }
        catch (Exception ex)
        {
            _failure = "Climbable Ladders body replacement compatibility failed: " + ex.GetBaseException().Message;
            CoreLog.LogWarning(_failure);
        }
        return _failure;
    }

    internal static Component[] Capture(LocalGame game, LocalPlayer body)
    {
        if (_controller == null) return null;
        RequireOutgoing(game, body);
        var controllers = body.gameObject.GetComponents(_controller);
        Validate(controllers, body);
        return controllers;
    }

    private static void RequireOutgoing(LocalGame game, LocalPlayer body)
    {
        if (!BodyReplacementContext.IsCreating || !BodyReplacementContext.OwnsRaid(game, game?.GameWorld)
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, body) || !BodyReplacementContext.OwnsBody(body)
            || !ReferenceEquals(game.LocalPlayer, body) || !ReferenceEquals(game.GameWorld.MainPlayer, body))
            throw new InvalidOperationException("Climbable Ladders dismount requires Core's outgoing local body.");
    }

    private static void Validate(Component[] controllers, LocalPlayer body)
    {
        foreach (var controller in controllers)
        {
            if (controller != null && (!ReferenceEquals(controller.gameObject, body.gameObject)
                || !ReferenceEquals(_player.GetValue(controller), body)))
                throw new InvalidOperationException("The active ladder controller does not belong to the outgoing player.");
        }
    }

    internal static void Dismount(LocalGame game, LocalPlayer body, Component[] controllers)
    {
        if (controllers == null || controllers.Length == 0) return;
        RequireOutgoing(game, body);
        Validate(controllers, body);
        foreach (var controller in controllers)
        {
            // OnDestroy cancels the approach, unsubscribes the old input delegate, and restores
            // the ladder overrides. A deferred Destroy could reset them after the successor climbs.
            if (controller == null) continue;
            UnityEngine.Object.DestroyImmediate(controller);
            if (controller != null)
                throw new InvalidOperationException("Climbable Ladders could not release the outgoing player's climb.");
        }
    }
}
