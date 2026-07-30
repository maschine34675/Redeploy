using CorpseRun.Config;
using CorpseRun.Services;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace CorpseRun.Patches;

/// <summary>
/// During respawn, skip GamePlayerOwner.CleanupOnDestroy so deferred Destroy does not
/// null MyPlayer or start CloseBattleUi after the new owner is created.
/// </summary>
internal static class RespawnOwnerCleanupPatch
{
    private static readonly FieldInfo OwnerDisposableField =
        AccessTools.Field(typeof(PlayerOwner), "OnDestroyCompositeDisposable");

    public static void Enable()
    {
        new OwnerCleanupPatch().Enable();
    }

    private sealed class OwnerCleanupPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => typeof(GamePlayerOwner).GetMethod(
                nameof(GamePlayerOwner.CleanupOnDestroy),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        [PatchPrefix]
        private static bool Prefix(GamePlayerOwner __instance)
        {
            if (!Settings.Enabled.Value || !RespawnSessionState.IsRespawnInProgress)
                return true;

            // Do not invoke CleanupOnDestroy virtually — that re-enters this prefix and overflows the stack.
            if (OwnerDisposableField.GetValue(__instance) is CompositeDisposableClass disposable)
                disposable.Dispose();

            return false;
        }
    }
}
