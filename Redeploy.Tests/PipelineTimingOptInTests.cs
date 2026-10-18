using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Redeploy.Tests;

/// <summary>
/// Executes the production collector and real request declaration against instrumented game
/// boundaries. This checks the disabled cost path, not merely whether its text was hidden.
/// </summary>
public class PipelineTimingOptInTests
{
    private static readonly string Root = ApiContractTests.RepoRoot;
    private static string TimingSource => File.ReadAllText(Path.Combine(Root, "Redeploy.Core", "Engine", "RespawnPipelineTiming.cs"));

    [Fact]
    public void CollectorIsInertByDefaultAndResumesOnlyForAnOptedInRequest()
        => Run(TimingSource);

    [Theory]
    [InlineData("if (request?.EnablePipelineTiming != true)", "if (false)", "disabled restore must not start a clock")]
    [InlineData("var active = _active;", "var active = _active; var ignored = profile?.EftStats?.Victims?.Count;",
        "disabled collector touched external state, reflected, logged or scheduled work")]
    public void TheCostProbeRejectsDisabledCollectionAndVictimReads(string anchor, string replacement, string expectedFailure)
    {
        var source = TimingSource;
        Assert.Contains(anchor, source);
        var failure = Assert.Throws<TargetInvocationException>(() => Run(source.Replace(anchor, replacement)));
        Assert.Equal(expectedFailure, failure.InnerException?.Message);
    }

    [Fact]
    public void StatisticsRestoreStillRunsExactlyOnceOutsideDiagnosticConditions()
    {
        var source = File.ReadAllText(Path.Combine(Root, "Redeploy.Core", "Engine", "DeathInterception.cs"));
        Assert.True(HasUnconditionalRestore(source));
        const string anchor = "RaidVictimsSnapshot.Restore(__instance.Profile);";
        Assert.Contains(anchor, source);
        Assert.False(HasUnconditionalRestore(source.Replace(anchor, "if (started != 0) " + anchor)));
        Assert.False(HasUnconditionalRestore(source.Replace(anchor, "")));
        Assert.False(HasUnconditionalRestore(source.Replace(anchor, anchor + anchor)));
    }

    private static bool HasUnconditionalRestore(string source)
    {
        var type = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
            .OfType<ClassDeclarationSyntax>().Single(t => t.Identifier.ValueText == "StatisticsRestorePatch");
        var method = type.Members.OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == "Postfix");
        var calls = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(n => n.Expression.ToString() == "RaidVictimsSnapshot.Restore").ToList();
        return calls.Count == 1 && calls[0].Parent is ExpressionStatementSyntax statement
            && ReferenceEquals(statement.Parent, method.Body);
    }

    private static void Run(string timing)
    {
        var engine = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(Root, "Redeploy.Core", "Engine", "RespawnEngine.cs"))).GetRoot();
        var request = engine.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Single(t => t.Identifier.ValueText == "RespawnRequest").ToFullString();
        var stages = File.ReadAllText(Path.Combine(Root, "Redeploy.Core", "Engine", "PipelineStages.cs"));
        var stubs = File.ReadAllText(Path.Combine(Root, "Redeploy.Tests", "Fixtures", "PipelineTimingGameBoundary.cs.txt"));
        var sources = new[] { timing, stages, stubs,
            "using System; using EFT; using Redeploy.Core.Spawning; namespace Redeploy.Core.Engine { " + request + " }" };
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("TimingProbe_" + Guid.NewGuid().ToString("N"),
            sources.Select(s => CSharpSyntaxTree.ParseText(s)), references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var emit = compilation.Emit(output);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        var assembly = Assembly.Load(output.ToArray());
        assembly.GetType("TimingProbe").GetMethod("Run").Invoke(null, null);
    }
}
