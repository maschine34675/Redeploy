using EFT;
using HarmonyLib;
using System;
using System.Reflection;

namespace CorpseRun.Services;

internal static class OwnerCleanupHelper
{
    private static readonly MethodInfo PlayerOwnerStopMethod =
        AccessTools.Method(typeof(PlayerOwner), "vmethod_1");

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

        UnityEngine.Object.Destroy(owner);
    }
}
