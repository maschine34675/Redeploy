using EFT;
using HarmonyLib;
using System.Reflection;

namespace CorpseRun.Services;

internal static class OwnerCleanupHelper
{
    private static readonly MethodInfo PlayerOwnerStopMethod =
        AccessTools.Method(typeof(PlayerOwner), "vmethod_1");

    /// <summary>
    /// Stops input handling without triggering GamePlayerOwner.CloseBattleUi,
    /// which would toggle BattleUI off a few frames later and break Escape menu.
    /// </summary>
    public static void DestroyOwnerWithoutClosingBattleUi(EftGamePlayerOwner owner, LocalPlayer player)
    {
        if (owner == null)
            return;

        if (owner.State == PlayerOwner.EState.Started)
            PlayerOwnerStopMethod.Invoke(owner, null);

        if (player != null)
            player.HasGamePlayerOwner = false;

        UnityEngine.Object.Destroy(owner);
    }
}
