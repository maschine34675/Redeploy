using EFT;
using HarmonyLib;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Redeploy.Core.GameAccess;

public static class OwnerCleanupHelper
{
    private static readonly MethodInfo PlayerOwnerStopMethod =
        AccessTools.Method(typeof(PlayerOwner), "vmethod_1");

    // Owners the pipeline retired. Their deferred Destroy resolves at the end of the
    // replacing frame, when the pipeline flag is already down and only the callers' own
    // flags still guard vanilla CleanupOnDestroy (which nulls MyPlayer for the NEW body).
    // Marking the instance makes the guard independent of those flags.
    private static readonly ConditionalWeakTable<GamePlayerOwner, object> Retired =
        new ConditionalWeakTable<GamePlayerOwner, object>();

    private static readonly object RetiredMarker = new object();

    /// <summary>True for an owner that <see cref="DestroyOwnerWithoutClosingBattleUi"/> retired.</summary>
    public static bool IsRetired(GamePlayerOwner owner)
        => owner != null && Retired.TryGetValue(owner, out _);

    /// <summary>
    /// Stops input handling without triggering GamePlayerOwner.CloseBattleUi,
    /// which would toggle BattleUI off a few frames later and break Escape menu.
    /// MethodInfo.Invoke would dispatch virtually into the GamePlayerOwner override
    /// (which starts the CloseBattleUi coroutine on StaticManager, surviving the owner's
    /// destruction) — so bind a delegate to the base method's function pointer for a
    /// true non-virtual call. Base vmethod_1 sets State = Stopped, which also makes the
    /// later Destroy -> CleanupOnDestroy -> virtual vmethod_1 chain early-return.
    /// </summary>
    public static void DestroyOwnerWithoutClosingBattleUi(EftGamePlayerOwner owner, LocalPlayer player)
    {
        if (owner == null)
            return;

        if (owner.State == PlayerOwner.EState.Started)
        {
            var baseStop = (Action)Activator.CreateInstance(
                typeof(Action),
                owner,
                PlayerOwnerStopMethod.MethodHandle.GetFunctionPointer());
            baseStop();
        }

        if (player != null)
            player.HasGamePlayerOwner = false;

        if (!Retired.TryGetValue(owner, out _))
            Retired.Add(owner, RetiredMarker);

        UnityEngine.Object.Destroy(owner);
    }
}
