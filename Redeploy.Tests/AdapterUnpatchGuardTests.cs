using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Redeploy.Tests;

/// <summary>
/// Every optional-mod adapter under Redeploy.Core/Compat calls <c>Harmony.UnpatchSelf()</c> only while
/// its own Harmony instance may hold patches. HarmonyX's UnpatchSelf (UnpatchID) walks every patched
/// method of the process with a GetPatchInfo allocation per method (about 1,270 methods, 2 ms and 10 MB
/// per walk on the GPU-2 host, 2026-10-31); ArenaMode polls GetRejectionReason from its map screen five
/// times a second, and every adapter of a mod that is NOT installed used to clear (unpatch) on each
/// poll. The adapters cannot load outside the game (EFT/BepInEx types), so this is a syntax contract,
/// checked with Roslyn like <see cref="SourceGuardTests"/>:
/// <list type="number">
/// <item>each <c>X.UnpatchSelf()</c> sits under <c>if (F)</c> or after <c>if (!F) return;</c> for a static
/// bool field F of the adapter;</item>
/// <item>the method holding that UnpatchSelf also assigns <c>F = false</c> (cleared when it unpatches);</item>
/// <item>every <c>X.Patch(...)</c> of the same Harmony instance is preceded, in its method, by <c>F = true</c>,
/// or sits in a helper whose every call site is (set where the adapter patches, so a bind that fails
/// between two Patch calls still unpatches).</item>
/// </list>
/// The fault tests remove the guard, the set and the clear of each named file in turn; the oracle has to
/// report every one of them, so a guard that stops seeing a file fails here instead of passing quietly.
/// </summary>
public class AdapterUnpatchGuardTests
{
    private static readonly string CompatRoot = Path.Combine(ApiContractTests.RepoRoot, "Redeploy.Core", "Compat");

    /// <summary>Every Core file that owns a Harmony instance and unpatches it (2026-10-31). A new adapter
    /// with UnpatchSelf must be added here, which makes its author read this contract.</summary>
    public static readonly string[] AdapterFiles =
    {
        "AmandsGraphics/AmandsGraphicsCompat.cs",
        "AudioIndicators/AudioIndicatorsCompat.cs",
        "ContinuousHealingCompat.cs",
        "ContinuousLoadAmmoCompat.cs",
        "EnemyMarkers/EnemyMarkersCompat.cs",
        "FovFixCompat.cs",
        "HealingAutoCancel/HealingAutoCancelCompat.cs",
        "ImpactCue/ImpactCueCompat.cs",
        "MagazineCheckCompat.cs",
        "ManimalAmmoCompat.cs",
        "PipDisabler/PipDisablerCompat.cs",
        "QuestTracker/QuestTrackerCompat.cs",
        "QuickCancel/QuickCancelCompat.cs",
        "Radar/RadarCompat.cs",
        "RecoilLegacyCompat.cs",
        "SimpleCrosshair/SimpleCrosshairCompat.cs",
    };

    public static IEnumerable<object[]> Adapters() => AdapterFiles.Select(file => new object[] { file });

    [Fact]
    public void TheAdapterListNamesEveryCompatFileThatUnpatches()
    {
        var actual = Directory.GetFiles(CompatRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("UnpatchSelf"))
            .Select(path => Path.GetRelativePath(CompatRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(AdapterFiles.OrderBy(path => path, StringComparer.Ordinal), actual);
    }

    [Theory]
    [MemberData(nameof(Adapters))]
    public void UnpatchSelfRunsOnlyWhileTheAdapterMayHoldPatches(string file)
    {
        var violations = UnpatchGuard.Violations(Read(file));
        Assert.True(violations.Count == 0, file + ":\n" + string.Join("\n", violations));
    }

    [Theory]
    [MemberData(nameof(Adapters))]
    public void RemovingTheGuardIsCaught(string file)
    {
        var faulted = UnpatchGuard.WithoutGuard(Read(file));
        Assert.Contains(UnpatchGuard.Violations(faulted), v => v.Contains("unguarded UnpatchSelf"));
    }

    [Theory]
    [MemberData(nameof(Adapters))]
    public void NeverSettingTheFlagIsCaught(string file)
    {
        var faulted = UnpatchGuard.WithoutAssignment(Read(file), value: true);
        Assert.Contains(UnpatchGuard.Violations(faulted), v => v.Contains("is not set before"));
    }

    [Theory]
    [MemberData(nameof(Adapters))]
    public void NeverClearingTheFlagIsCaught(string file)
    {
        var faulted = UnpatchGuard.WithoutAssignment(Read(file), value: false, onlyInUnpatchingMethod: true);
        Assert.Contains(UnpatchGuard.Violations(faulted), v => v.Contains("never cleared"));
    }

    [Fact]
    public void ThePre1019AbsentPathShapeIsCaught()
    {
        // QuickCancelCompat as it shipped in Core 1.0.18: the absent branch called Clear() on every poll
        // and Clear() unpatched unconditionally.
        const string source = """
            internal static class QuickCancelCompat
            {
                private static readonly Harmony Patches = new("id");
                internal static string GetRejectionReason()
                {
                    if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info)) { Clear(); return null; }
                    Patches.Patch(_mark, prefix: Hook(nameof(BeforeMark)));
                    return null;
                }
                private static void Clear() { Patches.UnpatchSelf(); _instance = null; }
            }
            """;
        Assert.Contains(UnpatchGuard.Violations(source), v => v.Contains("unguarded UnpatchSelf"));
    }

    private static string Read(string file) => File.ReadAllText(Path.Combine(CompatRoot, file));
}

/// <summary>The oracle behind <see cref="AdapterUnpatchGuardTests"/>, plus the faults it must catch.</summary>
internal static class UnpatchGuard
{
    public static List<string> Violations(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var violations = new List<string>();
        var harmonies = root.DescendantNodes().OfType<FieldDeclarationSyntax>()
            .Where(f => f.Declaration.Type.ToString() == "Harmony" && f.Modifiers.Any(SyntaxKind.StaticKeyword))
            .SelectMany(f => f.Declaration.Variables.Select(v => v.Identifier.ValueText))
            .ToList();
        if (harmonies.Count != 1)
        {
            violations.Add($"expected exactly one static Harmony field, found {harmonies.Count}");
            return violations;
        }
        var harmony = harmonies[0];
        var unpatches = Calls(root, harmony, "UnpatchSelf").ToList();
        if (unpatches.Count == 0) violations.Add($"{harmony}.UnpatchSelf() not found");

        var flags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unpatch in unpatches)
        {
            var method = Owner(unpatch);
            var flag = GuardFlag(unpatch);
            if (flag == null || !IsStaticBoolField(root, flag))
            {
                violations.Add($"unguarded UnpatchSelf in {Name(method)}: it must sit under if (flag) or after if (!flag) return;");
                continue;
            }
            flags.Add(flag);
            if (!Assignments(method, flag, false).Any())
                violations.Add($"{flag} is never cleared in {Name(method)}, which unpatches");
        }

        foreach (var flag in flags)
            foreach (var patch in Calls(root, harmony, "Patch"))
                if (!SetBefore(root, patch, flag, depth: 0))
                    violations.Add($"{flag} is not set before {harmony}.Patch in {Name(Owner(patch))} (line {Line(patch)})");
        return violations;
    }

    /// <summary>Unwrap <c>if (F) ...UnpatchSelf...</c> or delete <c>if (!F) return;</c> before it.</summary>
    public static string WithoutGuard(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var harmony = root.DescendantNodes().OfType<FieldDeclarationSyntax>()
            .First(f => f.Declaration.Type.ToString() == "Harmony").Declaration.Variables[0].Identifier.ValueText;
        var unpatch = Calls(root, harmony, "UnpatchSelf").First();
        var guard = unpatch.Ancestors().TakeWhile(node => node is not MemberDeclarationSyntax)
            .OfType<IfStatementSyntax>().FirstOrDefault(s => s.Condition is IdentifierNameSyntax && s.Statement.Span.Contains(unpatch.Span));
        if (guard != null)
            return root.ReplaceNode(guard, guard.Statement).ToFullString();
        var early = EarlyReturnGuards(unpatch).First();
        return root.RemoveNode(early, SyntaxRemoveOptions.KeepNoTrivia)!.ToFullString();
    }

    /// <summary>Delete every <c>F = value</c> statement for the guard flag F (only in the unpatching method when asked).</summary>
    public static string WithoutAssignment(string source, bool value, bool onlyInUnpatchingMethod = false)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var harmony = root.DescendantNodes().OfType<FieldDeclarationSyntax>()
            .First(f => f.Declaration.Type.ToString() == "Harmony").Declaration.Variables[0].Identifier.ValueText;
        var unpatch = Calls(root, harmony, "UnpatchSelf").First();
        var flag = GuardFlag(unpatch) ?? throw new InvalidOperationException("no guard flag to remove");
        SyntaxNode scope = onlyInUnpatchingMethod ? Owner(unpatch) : root;
        var targets = Assignments(scope, flag, value)
            .Select(a => (SyntaxNode)(a.Parent is ExpressionStatementSyntax s ? s : a))
            .ToList();
        if (targets.Count == 0) throw new InvalidOperationException($"no {flag} = {value} to remove");
        // A statement removed from an embedded position (if (x) F = false;) becomes an empty statement.
        return root.ReplaceNodes(targets, (original, _) => original is ExpressionStatementSyntax
            ? SyntaxFactory.EmptyStatement()
            : SyntaxFactory.IdentifierName(flag)).ToFullString();
    }

    private static IEnumerable<InvocationExpressionSyntax> Calls(SyntaxNode root, string receiver, string method)
        => root.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(call =>
            call.Expression is MemberAccessExpressionSyntax access
            && access.Name.Identifier.ValueText == method
            && access.Expression is IdentifierNameSyntax target && target.Identifier.ValueText == receiver);

    private static string GuardFlag(InvocationExpressionSyntax unpatch)
    {
        foreach (var guard in unpatch.Ancestors().TakeWhile(node => node is not MemberDeclarationSyntax).OfType<IfStatementSyntax>())
            if (guard.Condition is IdentifierNameSyntax flag && guard.Statement.Span.Contains(unpatch.Span))
                return flag.Identifier.ValueText;
        var early = EarlyReturnGuards(unpatch).FirstOrDefault();
        return early == null ? null : ((IdentifierNameSyntax)((PrefixUnaryExpressionSyntax)early.Condition).Operand).Identifier.ValueText;
    }

    /// <summary><c>if (!F) return;</c> statements that precede the unpatch in one of its enclosing blocks.</summary>
    private static IEnumerable<IfStatementSyntax> EarlyReturnGuards(InvocationExpressionSyntax unpatch)
    {
        foreach (var block in unpatch.Ancestors().TakeWhile(node => node is not MemberDeclarationSyntax).OfType<BlockSyntax>())
            foreach (var statement in block.Statements.TakeWhile(s => s.SpanStart < unpatch.SpanStart && !s.Span.Contains(unpatch.Span)))
                if (statement is IfStatementSyntax { Else: null } guard
                    && guard.Condition is PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression, Operand: IdentifierNameSyntax }
                    && (guard.Statement is ReturnStatementSyntax { Expression: null }
                        || guard.Statement is BlockSyntax { Statements.Count: 1 } only
                           && only.Statements[0] is ReturnStatementSyntax { Expression: null }))
                    yield return guard;
    }

    private static bool SetBefore(CompilationUnitSyntax root, SyntaxNode site, string flag, int depth)
    {
        var owner = Owner(site);
        if (owner == null) return false;
        if (Assignments(owner, flag, true).Any(a => a.SpanStart < site.SpanStart)) return true;
        // A helper (Patch(...), AddUnregister(...)) is fine when every call site sets the flag first.
        var name = Name(owner);
        if (depth >= 3 || name == null) return false;
        // Only plain calls count: Patches.Patch(...) is the Harmony call itself, never a call of a helper named Patch.
        var callers = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(call => call.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == name
                           && !owner.Span.Contains(call.Span))
            .ToList();
        return callers.Count > 0 && callers.All(call => SetBefore(root, call, flag, depth + 1));
    }

    private static IEnumerable<AssignmentExpressionSyntax> Assignments(SyntaxNode scope, string flag, bool value)
        => scope.DescendantNodes().OfType<AssignmentExpressionSyntax>().Where(a =>
            a.IsKind(SyntaxKind.SimpleAssignmentExpression)
            && a.Left is IdentifierNameSyntax left && left.Identifier.ValueText == flag
            && RightmostValue(a) is LiteralExpressionSyntax literal
            && literal.IsKind(value ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression));

    // F = G = false assigns false to F too.
    private static ExpressionSyntax RightmostValue(AssignmentExpressionSyntax assignment)
        => assignment.Right is AssignmentExpressionSyntax chained ? RightmostValue(chained) : assignment.Right;

    private static bool IsStaticBoolField(SyntaxNode root, string name)
        => root.DescendantNodes().OfType<FieldDeclarationSyntax>().Any(f =>
            f.Modifiers.Any(SyntaxKind.StaticKeyword) && f.Declaration.Type.ToString() == "bool"
            && f.Declaration.Variables.Any(v => v.Identifier.ValueText == name));

    private static SyntaxNode Owner(SyntaxNode node)
        => node.Ancestors().FirstOrDefault(a => a is LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax or AccessorDeclarationSyntax);

    private static string Name(SyntaxNode owner) => owner switch
    {
        MethodDeclarationSyntax m => m.Identifier.ValueText,
        LocalFunctionStatementSyntax l => l.Identifier.ValueText,
        _ => null,
    };

    private static int Line(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
}
