namespace Redeploy.Core.Engine;

/// <summary>
/// The engine's two dispatchers. <see cref="Respawn"/> carries the death and pipeline
/// raises; its depth is what the RunRespawn preflight consults, so a handler can never
/// start a replacement from inside one. <see cref="Lifecycle"/> carries the raid
/// boundaries and never blocks a RunRespawn (ArenaMode starts its raid-start swap from
/// inside OnRaidStarted).
/// </summary>
internal static class CoreSignals
{
    public static readonly SignalDispatcher Respawn = new SignalDispatcher(CoreLog.LogWarning, IsDestroyedUnityObject);

    public static readonly SignalDispatcher Lifecycle = new SignalDispatcher(CoreLog.LogWarning, IsDestroyedUnityObject);

    // Unity's overloaded == is the point: a destroyed MonoBehaviour is a live C# object.
    private static bool IsDestroyedUnityObject(object target)
        => target is UnityEngine.Object unityObject && unityObject == null;
}
