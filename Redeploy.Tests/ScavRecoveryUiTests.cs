using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Redeploy.Tests;

public sealed class ScavRecoveryUiTests
{
    private static string ServiceSource => File.ReadAllText(Path.Combine(ApiContractTests.RepoRoot,
        "Redeploy.Client", "Services", "ScavRecoveryClient.cs"));
    private static readonly Lazy<Type> Harness = new(BuildHarness);

    [Theory]
    [InlineData("ordinary-menu-silent")]
    [InlineData("automatic-status-once")]
    [InlineData("no-action-before-status")]
    [InlineData("double-abandon-rejected")]
    [InlineData("double-transfer-rejected")]
    [InlineData("stale-menu-response")]
    [InlineData("stale-session-response")]
    [InlineData("status-failure-retry")]
    [InlineData("mutation-failure-stays-fenced")]
    [InlineData("lost-abandon-ack-status-none-restarts")]
    [InlineData("restart-survives-menu-reopen")]
    [InlineData("new-session-clears-restart")]
    [InlineData("missing-server-dismiss-no-spam")]
    [InlineData("missing-server-new-menu-no-spam")]
    [InlineData("malformed-status-rejected")]
    [InlineData("completed-transfer-cannot-repeat")]
    [InlineData("transfer-identity")]
    [InlineData("inventory-error-invalidates-refresh")]
    [InlineData("recorded-save-can-return-transfer")]
    [InlineData("blocked-status-cannot-dismiss")]
    public void ActualProductionFlowHonorsRecoveryOwnership(string scenario)
        => Assert.Equal("PASS", Harness.Value.GetMethod("Run")!.Invoke(null, new object[] { scenario }));

    [Fact]
    public void NativeTransferLoadsCurrentInventoriesAfterFlushingAndNeverUsesRaidResultSave()
    {
        var source = ServiceSource;
        Assert.True(HasAuthoritativeTransferBoundary(source));
        // A skipped refresh would let stale or already-transferred loot be reused.
        Assert.False(HasAuthoritativeTransferBoundary(source.Replace("await session.GetProfiles();", "")));
        // Reversing these awaits exposes a profile replacement/inventory update race.
        var mutant = source.Replace("await session.FlushOperationQueue()", "await session.GetProfiles()")
            .Replace("await session.GetProfiles();", "await session.FlushOperationQueue();");
        Assert.False(HasAuthoritativeTransferBoundary(mutant));
        Assert.DoesNotContain("SessionResultShowOperation", source);
        Assert.DoesNotContain("ProfileDescriptor", source);
        Assert.DoesNotContain("session.ProfileOfPet =", source);
    }

    [Fact]
    public void DestructiveActionIsNotReachableFromReadinessAndFinishingUsesNativeQueue()
    {
        string source = ServiceSource;
        var tick = Method(source, "Tick");
        Assert.Contains("CheckStatus(false)", tick);
        Assert.DoesNotContain("Abandon(", tick);
        Assert.DoesNotContain("ResumeTransfer(", tick);
        var finish = Method(source, "FinishTransfer");
        Assert.True(Ordered(finish, "TryCloseAllScreens()", "session.FlushOperationQueue()", "session.FinishScavSession()", "Flow.RequireRestart(revision)"));
        Assert.Contains("Flow.BeginAction(\"transfer\")", finish);
        Assert.Contains("ReferenceEquals(EftScreenManager.Instance.CurrentScreenController, transfer)", finish);
        Assert.Contains("Current(revision, session, menu)", finish);
    }

    [Fact]
    public void HooksRestrictToRealMenuAndFenceReadyCallbacksBeforeEnablingUi()
    {
        string source = ServiceSource;
        var menuShown = Method(source, "MenuShown");
        Assert.Contains("InGameStatus.InRaid", menuShown);
        Assert.Contains("controller is MenuScreen.MainMenuScreenController", menuShown);
        var patch = File.ReadAllText(Path.Combine(ApiContractTests.RepoRoot, "Redeploy.Client", "Patches", "ScavRecoveryMenuPatch.cs"));
        Assert.True(Ordered(Method(patch, "EnablePatches"), "new MenuSelectionPatch().Enable()", "new RaidReadyPatch().Enable()", "new MenuShownPatch().Enable()", "Ready = true"));
        Assert.Contains("ScavRecoveryClient.BlocksMenu(__instance)", patch);
        Assert.Contains("__result = Task.CompletedTask", patch);
        var panel = File.ReadAllText(Path.Combine(ApiContractTests.RepoRoot, "Redeploy.Client", "UI", "ScavRecoveryPanel.cs"));
        Assert.Contains("!ScavRecoveryClient.IsMainMenu", panel);
        Assert.Contains("Confirm — abandon interrupted raid", panel);
        Assert.Contains("Resume loot transfer", panel);
    }

    private static bool HasAuthoritativeTransferBoundary(string source)
    {
        string resume = Method(source, "ResumeTransfer");
        return Ordered(resume, "session.FlushOperationQueue()", "await session.GetProfiles();", "var pmc = session.Profile;",
                "ScavRecoveryFlow.MatchesTransfer(", "new OfflineInventoryController(session, pmc", "ProfileUpdatesHandler.Bind(pmc",
                "new ScavengerInventoryScreen.ScavengerInventoryScreenController", "transfer.ShowScreenAsync(EScreenState.Root)")
            && resume.Split("Current(revision, session, menu, true)").Length >= 3;
    }

    private static bool Ordered(string source, params string[] markers)
    {
        int index = -1;
        foreach (var marker in markers)
        {
            index = source.IndexOf(marker, index + 1, StringComparison.Ordinal);
            if (index < 0) return false;
        }
        return true;
    }

    private static string Method(string source, string name)
        => CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == name).ToFullString();

    private static Type BuildHarness()
    {
        var policy = CSharpSyntaxTree.ParseText(ServiceSource).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText == "ScavRecoveryFlow").ToFullString();
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ScavRecoveryUi_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText("using System;\n" + policy + Boundary) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return Assembly.Load(output.ToArray()).GetType("RecoveryHarness", throwOnError: true)!;
    }

    private const string Boundary = """
        public static class RecoveryHarness {
            private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
            public static string Run(string scenario) {
                var flow = new ScavRecoveryFlow();
                var session = new object(); var menu = new object();
                flow.Attach(session, menu);
                if (scenario == "no-action-before-status") {
                    Check(flow.BeginAction("abandon") < 0 && flow.BeginAction("transfer") < 0, "action without status");
                    return "PASS";
                }
                int revision = flow.BeginStatus(false);
                Check(revision >= 0 && flow.Busy && flow.BlocksMenu, "initial check");
                if (scenario == "automatic-status-once") {
                    Check(flow.BeginStatus(false) < 0, "concurrent status");
                    flow.Accept(revision, "none", null, null);
                    flow.Attach(session, menu);
                    Check(flow.BeginStatus(false) < 0, "polling ready menu");
                    flow.Attach(session, new object());
                    Check(flow.BeginStatus(false) >= 0, "new lifecycle check");
                    return "PASS";
                }
                if (scenario == "stale-menu-response" || scenario == "stale-session-response") {
                    var nextSession = scenario == "stale-session-response" ? new object() : session;
                    var nextMenu = new object(); flow.Attach(nextSession, nextMenu);
                    int nextRevision = flow.BeginStatus(false);
                    Check(!flow.Owns(revision, session, menu), "old owner retained");
                    Check(!flow.Accept(revision, "abandon", "old", null), "old response accepted");
                    flow.Fail(revision, "old error");
                    Check(flow.Busy && flow.Error == null, "old error replaced new request");
                    flow.Accept(nextRevision, "none", null, null);
                    Check(!flow.Visible, "old menu stole new menu");
                    return "PASS";
                }
                if (scenario == "status-failure-retry" || scenario == "missing-server-dismiss-no-spam") {
                    flow.Fail(revision, "unavailable");
                    Check(flow.Visible && !flow.BlocksMenu && flow.BeginStatus(false) < 0, "failed status auto retry");
                    if (scenario == "missing-server-dismiss-no-spam") {
                        flow.DismissUnavailable(); Check(!flow.Visible && flow.BeginStatus(false) < 0, "missing endpoint spam");
                    } else {
                        int retry = flow.BeginStatus(true); Check(retry >= 0, "no explicit retry");
                        flow.Accept(retry, "abandon", "recovery", null); Check(flow.BlocksMenu, "known pending not fenced");
                    }
                    return "PASS";
                }
                if (scenario == "missing-server-new-menu-no-spam") {
                    flow.MissingBackend(revision, "missing route"); flow.DismissUnavailable();
                    flow.Attach(session, new object());
                    Check(flow.BeginStatus(false) < 0 && !flow.Visible && !flow.BlocksMenu, "missing backend polled on menu reopen");
                    Check(flow.BeginStatus(true) >= 0, "missing backend explicit retry unavailable"); return "PASS";
                }
                if (scenario == "malformed-status-rejected") {
                    bool rejected = false;
                    try { flow.Accept(revision, "unknown", "id", null); } catch (InvalidOperationException) { rejected = true; }
                    Check(rejected && flow.Busy, "unknown state accepted"); rejected = false;
                    try { flow.Accept(revision, "transfer", "", null); } catch (InvalidOperationException) { rejected = true; }
                    Check(rejected, "missing identity accepted"); return "PASS";
                }
                if (scenario == "ordinary-menu-silent") {
                    flow.Accept(revision, "none", null, null);
                    Check(!flow.Visible && !flow.BlocksMenu && !flow.Busy, "ordinary menu altered"); return "PASS";
                }
                if (scenario == "blocked-status-cannot-dismiss") {
                    flow.Accept(revision, "blocked", null, "protected");
                    flow.DismissUnavailable();
                    Check(flow.Visible && flow.BlocksMenu && flow.BeginAction("abandon") < 0, "blocked status escaped");
                    Check(flow.BeginStatus(true) >= 0, "blocked status not retryable"); return "PASS";
                }
                if (scenario == "recorded-save-can-return-transfer") {
                    flow.Accept(revision, "abandon", "save-id", "saved extraction", "save");
                    Check(flow.Action == "save", "save misrepresented as death");
                    int save = flow.BeginAction("abandon"); flow.ProfilesMayHaveChanged(save);
                    flow.Accept(save, "transfer", "save-id", "remaining loot");
                    Check(flow.State == "transfer" && flow.BeginAction("transfer") >= 0, "recorded extraction lost transfer"); return "PASS";
                }
                if (scenario == "transfer-identity") {
                    Check(ScavRecoveryFlow.MatchesTransfer("pmc", "scav", "root", "scav", "root"), "valid rejected");
                    Check(!ScavRecoveryFlow.MatchesTransfer("pmc", "scav", "newroot", "scav", "root"), "regenerated root accepted");
                    Check(!ScavRecoveryFlow.MatchesTransfer("pmc", "other", "root", "scav", "root"), "foreign scav accepted");
                    Check(!ScavRecoveryFlow.MatchesTransfer("scav", "scav", "root", "scav", "root"), "pmc scav same identity");
                    Check(!ScavRecoveryFlow.MatchesTransfer("pmc", null, null, null, null), "missing identities accepted"); return "PASS";
                }
                string state = scenario.Contains("transfer") ? "transfer" : "abandon";
                flow.Accept(revision, state, "recovery", "pending");
                Check(flow.Visible && flow.BlocksMenu && !flow.Busy, "pending invisible");
                flow.DismissUnavailable(); Check(flow.Visible, "pending dismissed");
                int action = flow.BeginAction(state); Check(action >= 0, "action unavailable");
                Check(flow.BeginAction(state) < 0 && flow.BeginStatus(true) < 0, "double click accepted");
                if (scenario.StartsWith("double-")) return "PASS";
                flow.ProfilesMayHaveChanged(action);
                if (scenario == "mutation-failure-stays-fenced") {
                    flow.Fail(action, "lost result"); flow.DismissUnavailable();
                    Check(flow.Visible && flow.BlocksMenu && flow.BeginAction(state) < 0, "ambiguous mutation can repeat");
                    Check(flow.BeginStatus(true) >= 0, "ambiguous mutation not retryable"); return "PASS";
                }
                if (scenario == "inventory-error-invalidates-refresh") {
                    flow.Interrupt("native inventory error");
                    Check(!flow.Owns(action, session, menu), "old refresh still owns menu");
                    Check(!flow.Accept(action, "none", null, null), "old refresh cleared native error");
                    Check(flow.Error != null && flow.BlocksMenu && flow.BeginStatus(true) >= 0, "error not visible and retryable"); return "PASS";
                }
                if (scenario == "lost-abandon-ack-status-none-restarts") {
                    flow.Fail(action, "lost result"); int retry = flow.BeginStatus(true);
                    flow.Accept(retry, "none", null, null);
                    Check(flow.State == "restart" && flow.BlocksMenu, "stale profiles escaped"); return "PASS";
                }
                flow.RequireRestart(action);
                if (scenario == "completed-transfer-cannot-repeat") {
                    Check(flow.BeginAction("transfer") < 0 && flow.BeginStatus(true) < 0 && flow.BlocksMenu, "completed transfer repeated"); return "PASS";
                }
                if (scenario == "restart-survives-menu-reopen") {
                    flow.Attach(session, new object()); Check(flow.State == "restart" && flow.BlocksMenu, "restart fence lost"); return "PASS";
                }
                if (scenario == "new-session-clears-restart") {
                    flow.Attach(new object(), new object()); Check(flow.State == "none" && !flow.NeedsRestart, "old restart leaked"); return "PASS";
                }
                throw new Exception("Unknown scenario: " + scenario);
            }
        }
        """;
}
