using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Redeploy.Server.Patches;
using Redeploy.Server.Scav;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Services.InRaid;
using Xunit;

namespace Redeploy.Server.Tests;

public sealed partial class ScavContinuationTests
{
    private static int observedStarts;
    private static int observedLogins;
    private static bool observerThrows;
    private static bool observerTaskFails;

    private static bool ObserveRaidStart(ref Task<StartLocalRaidResponseData> __result)
    {
        observedStarts++;
        if (observerThrows) throw new InvalidOperationException("foreign prefix failed");
        __result = observerTaskFails
            ? Task.FromException<StartLocalRaidResponseData>(new IOException("native async start failed"))
            : Task.FromResult(new StartLocalRaidResponseData { ServerId = "factory4_day.pmc observed" });
        return false;
    }

    private static bool ObserveLogin(ref ValueTask<string> __result)
    {
        observedLogins++;
        if (observerThrows) throw new InvalidOperationException("foreign login prefix failed");
        __result = ValueTask.FromResult("observed-native-login");
        return false;
    }

    private static Harmony InstallLifecyclePatch(Type patchType, MethodInfo target, string observer)
    {
        var harmony = new Harmony("redeploy.lifecycle-test." + Guid.NewGuid().ToString("N"));
        harmony.Patch(target,
            prefix: new HarmonyMethod(patchType.GetMethod("Prefix")!) { priority = Priority.First },
            postfix: new HarmonyMethod(patchType.GetMethod("Postfix")!),
            finalizer: new HarmonyMethod(patchType.GetMethod("Finalizer")!));
        harmony.Patch(target, prefix: new HarmonyMethod(typeof(ScavContinuationTests).GetMethod(observer,
            BindingFlags.NonPublic | BindingFlags.Static)!) { priority = Priority.Last });
        return harmony;
    }

    [Theory] [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)]
    public async Task ActualHarmonyStartDispatchRunsForeignPrefixOnceAndReleasesOnSkipOrFailure(bool synchronousFailure, bool asyncFailure)
    {
        using var f = new Fixture();
        observedStarts = 0; observerThrows = synchronousFailure; observerTaskFails = asyncFailure;
        var target = typeof(LocationLifecycleService).GetMethod(nameof(LocationLifecycleService.StartLocalRaidAsync))!;
        var harmony = InstallLifecyclePatch(typeof(ScavRaidStartPatch), target, nameof(ObserveRaidStart));
        try
        {
            var native = (LocationLifecycleService)RuntimeHelpers.GetUninitializedObject(typeof(LocationLifecycleService));
            if (synchronousFailure)
                Assert.Throws<InvalidOperationException>(() => { _ = native.StartLocalRaidAsync(f.Session, new() { PlayerSide = "pmc" }); });
            else if (asyncFailure)
                await Assert.ThrowsAsync<IOException>(() => native.StartLocalRaidAsync(f.Session, new() { PlayerSide = "pmc" }));
            else
                Assert.Equal("factory4_day.pmc observed", (await native.StartLocalRaidAsync(f.Session, new() { PlayerSide = "pmc" })).ServerId);
            Assert.Equal(1, observedStarts);
            observerThrows = observerTaskFails = false;
            // A second actual patched call would hang if either release path
            // leaked the account gate; it also catches over-release on finalizer.
            var retry = Task.Run(() => native.StartLocalRaidAsync(f.Session, new() { PlayerSide = "pmc" }));
            Assert.Equal("factory4_day.pmc observed", (await retry.WaitAsync(TimeSpan.FromSeconds(3))).ServerId);
            Assert.Equal(2, observedStarts);
        }
        finally { harmony.UnpatchSelf(); observerThrows = observerTaskFails = false; }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task ActualHarmonyLoginDispatchRunsForeignPrefixOnceAndReleasesInterruptedProfile(bool synchronousFailure)
    {
        using var f = new Fixture(); var entry = f.Entry(); entry.Phase = ScavPhase.Active; f.Journal.Write(entry);
        observedLogins = 0; observerThrows = synchronousFailure;
        var target = typeof(GameCallbacks).GetMethod(nameof(GameCallbacks.GameStart))!;
        var harmony = InstallLifecyclePatch(typeof(ScavRecoveryGameStartPatch), target, nameof(ObserveLogin));
        try
        {
            var native = (GameCallbacks)RuntimeHelpers.GetUninitializedObject(typeof(GameCallbacks));
            if (synchronousFailure)
                Assert.Throws<InvalidOperationException>(() => native.GameStart("/client/game/start", new EmptyRequestData(), f.Session));
            else
                Assert.Equal("observed-native-login", await native.GameStart("/client/game/start", new EmptyRequestData(), f.Session));
            Assert.Equal(1, observedLogins);
            observerThrows = false;
            var status = await f.Service.RecoveryStatus(f.Session, default).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("abandon", status.State); Assert.Equal(0, f.Writes); Assert.Empty(f.NativeStages);
            Assert.Equal(ScavPhase.Active, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
        }
        finally { harmony.UnpatchSelf(); observerThrows = false; }
    }
}
