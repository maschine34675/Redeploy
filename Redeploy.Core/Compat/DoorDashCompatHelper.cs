using BepInEx.Bootstrap;
using EFT;
using System;
using System.Reflection;
using UnityEngine;

namespace Redeploy.Core.Compat;

/// <summary>
/// DoorDash (com.tarkin.doordash) runs its sprint ram from a RaycastBreacher component that it
/// adds to the main player exactly once, in its GameWorld.OnGameStarted postfix. A replacement
/// body is created long after that and never gets one, so ramming silently stops with the
/// first respawn or kit swap. This carries the component over: off the previous body (rule
/// 4.4.2 in docs/RESPAWN-API.md; a swapped-out body is still alive, and with DoorDash switched
/// off its own "player is dead" self-destruct never runs), fresh onto the new one. The
/// component looks its player up itself (GetComponent in Awake), so nothing of DoorDash's is
/// written. No-op when DoorDash isn't loaded.
/// </summary>
internal static class DoorDashCompatHelper
{
    private const string PluginGuid = "com.tarkin.doordash";
    private const string BreacherTypeName = "tarkin.doordash.RaycastBreacher";
    private const string EnabledSwitchName = "Enabled";

    private static bool _reportedFollowing;
    private static bool _reportedStandDown;
    private static bool _reportedFailure;

    /// <summary>
    /// Called from the rewire block of RunRespawn, so it must never throw: a throw there fails
    /// the respawn after its teardown.
    /// </summary>
    public static void FollowBody(Player previousBody, Player newBody)
    {
        try
        {
            if (newBody == null)
                return;

            // Resolved per respawn instead of cached: a dictionary lookup and two reflection
            // calls a few times per raid, and a cached Type would outlive a reloaded DoorDash.
            Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info);
            var plugin = info?.Instance;
            var pluginType = plugin != null ? plugin.GetType() : null;
            var breacherType = pluginType?.Assembly.GetType(BreacherTypeName, false);
            var enabledSwitch = pluginType?.GetField(EnabledSwitchName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            var decision = DoorDashFollowRule.Decide(
                pluginLoaded: pluginType != null,
                breacherFound: breacherType != null,
                breacherIsBehaviour: breacherType != null && typeof(MonoBehaviour).IsAssignableFrom(breacherType),
                hasEnabledSwitch: enabledSwitch != null,
                enabledSwitchBound: enabledSwitch?.GetValue(null) != null);

            if (decision == DoorDashFollow.NotInstalled)
                return;

            if (decision != DoorDashFollow.Follow)
            {
                ReportStandDown(decision, info.Metadata?.Version);
                return;
            }

            var leftBehind = 0;
            if (previousBody != null && !ReferenceEquals(previousBody, newBody))
                leftBehind = Remove(previousBody.gameObject, breacherType, immediate: false);

            // AttachFresh, never GetOrAdd: a pooled player object can bring a breacher along
            // from an earlier life.
            var body = newBody.gameObject;
            var stale = Remove(body, breacherType, immediate: true);
            body.AddComponent(breacherType);

            if (!_reportedFollowing)
            {
                _reportedFollowing = true;
                CoreLog.LogInfo($"DoorDash {info.Metadata?.Version} detected: its sprint-ram component follows respawned bodies.");
            }

            CoreLog.LogDebug($"DoorDash compat: breacher attached to the new body (removed {leftBehind} from the previous body, {stale} stale).");
        }
        catch (Exception ex)
        {
            if (_reportedFailure)
                return;

            _reportedFailure = true;
            CoreLog.LogWarning($"DoorDash compat failed; its sprint ram may stop working after a respawn: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int Remove(GameObject host, Type breacherType, bool immediate)
    {
        var breachers = host.GetComponents(breacherType);
        foreach (var breacher in breachers)
        {
            if (immediate)
                UnityEngine.Object.DestroyImmediate(breacher);
            else
                UnityEngine.Object.Destroy(breacher);
        }

        return breachers.Length;
    }

    private static void ReportStandDown(DoorDashFollow decision, System.Version version)
    {
        if (_reportedStandDown)
            return;

        _reportedStandDown = true;

        // A DoorDash that failed to start has reported that itself and rams nowhere; a DoorDash
        // this helper no longer fits is the case a player would otherwise search for.
        if (decision == DoorDashFollow.NotInitialised)
            CoreLog.LogInfo($"DoorDash {version} is loaded but did not finish starting; its sprint ram is left alone.");
        else
            CoreLog.LogWarning($"DoorDash {version} has no {BreacherTypeName} component; its sprint ram will stop working after a respawn.");
    }
}
