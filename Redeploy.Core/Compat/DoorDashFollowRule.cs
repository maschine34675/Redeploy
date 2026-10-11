namespace Redeploy.Core.Compat;

internal enum DoorDashFollow
{
    /// <summary>DoorDash is not loaded: nothing to do, nothing to say.</summary>
    NotInstalled,

    /// <summary>DoorDash is loaded, but its breacher is not the component this was written for.</summary>
    UnknownShape,

    /// <summary>DoorDash is loaded, but never got as far as binding its settings.</summary>
    NotInitialised,

    /// <summary>Move the breacher to the new body.</summary>
    Follow,
}

/// <summary>
/// When DoorDashCompatHelper moves DoorDash's breacher to a replacement body, pure so the
/// matrix is tested. The settings gate exists because the breacher reads DoorDash's static
/// settings every frame: a DoorDash whose startup failed has none, never attaches a breacher
/// itself, and one attached from here would throw in every Update. A DoorDash without that
/// switch (renamed, removed) is not held back by it: only a switch that exists and is unbound
/// says "not initialised".
/// </summary>
internal static class DoorDashFollowRule
{
    public static DoorDashFollow Decide(
        bool pluginLoaded,
        bool breacherFound,
        bool breacherIsBehaviour,
        bool hasEnabledSwitch,
        bool enabledSwitchBound)
    {
        if (!pluginLoaded)
            return DoorDashFollow.NotInstalled;

        if (!breacherFound || !breacherIsBehaviour)
            return DoorDashFollow.UnknownShape;

        if (hasEnabledSwitch && !enabledSwitchBound)
            return DoorDashFollow.NotInitialised;

        return DoorDashFollow.Follow;
    }
}
