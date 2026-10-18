using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Redeploy.Tests;

/// <summary>Executes the real Awake/lifecycle methods and version gate, replacing
/// only game-dependent runtime bodies and the assembly resolver with probes.
/// Native Mono startup and compiled client IL are verified separately.</summary>
public class CoreBootstrapTests
{
    [SkippableTheory]
    [InlineData(false, "missing", false)]
    [InlineData(false, "1.0.7.0", false)]
    [InlineData(false, "1.0.8.0", true)]
    [InlineData(false, "2.0.0.0", true)]
    [InlineData(true, "missing", false)]
    [InlineData(true, "1.0.7.0", false)]
    [InlineData(true, "1.0.8.0", true)]
    [InlineData(true, "2.0.0.0", true)]
    public void RealEntryPointsAdmitOnlySupportedCore(bool arena, string installed, bool accepted)
        => Probe(arena, installed, accepted);

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProbeDetectsMissingStartupReturn(bool arena)
    {
        RequireSource(arena);
        Assert.ThrowsAny<Exception>(() => Probe(arena, "missing", false, "return;", ""));
    }

    [SkippableTheory]
    [InlineData("if (_runtimeStarted) StartRuntime();", "StartRuntime();")]
    [InlineData("if (_runtimeStarted) UpdateRuntime();", "UpdateRuntime();")]
    [InlineData("if (!_runtimeStarted) return;", "")]
    public void ProbeDetectsUnguardedUnityCallbacks(string before, string after)
    {
        RequireSource(true);
        Assert.ThrowsAny<Exception>(() => Probe(true, "missing", false, before, after));
    }

    private static void RequireSource(bool arena)
        => Skip.If(arena && !File.Exists(Path.Combine(ApiContractTests.RepoRoot, "..", "ArenaMode", "ArenaMode.Client", "Plugin.cs")),
            "Arena-only integration case requires the sibling ArenaMode checkout");

    private static void Probe(bool arena, string installed, bool accepted, string before = null, string after = null)
    {
        RequireSource(arena);
        string repo = ApiContractTests.RepoRoot;
        string path = arena ? Path.Combine(repo, "..", "ArenaMode", "ArenaMode.Client", "Plugin.cs")
            : Path.Combine(repo, "Redeploy.Client", "Plugin.cs");
        Assert.True(File.Exists(path), "Bootstrap probe needs the sibling ArenaMode checkout");
        string source = File.ReadAllText(path);
        if (before != null) { Assert.Contains(before, source); source = source.Replace(before, after); }
        var original = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes()
            .OfType<ClassDeclarationSyntax>().Single(t => t.Identifier.ValueText == "Plugin");
        var members = original.Members.Select(member =>
        {
            if (member is not MethodDeclarationSyntax method || !method.Identifier.ValueText.EndsWith("Runtime")) return member;
            // Only these four audited boundaries are replaced; entry methods stay exact.
            Assert.Contains(method.Identifier.ValueText, new[] { "InitializeRuntime", "StartRuntime", "UpdateRuntime", "ShutdownRuntime" });
            Assert.Contains("NoInlining", method.AttributeLists.ToFullString());
            return method.WithExpressionBody(null).WithSemicolonToken(default)
                .WithBody(SyntaxFactory.Block(SyntaxFactory.ParseStatement("Boundary.Calls++;")));
        });
        var plugin = original.WithAttributeLists(default).WithMembers(SyntaxFactory.List(members));
        string gate = File.ReadAllText(Path.Combine(repo, "drivers", "CoreVersionGate.cs"));
        var gateTree = CSharpSyntaxTree.ParseText(gate).GetRoot();
        var resolver = gateTree.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.ValueText == "ResolveCoreIdentity");
        gateTree = gateTree.ReplaceNode(resolver, resolver.WithBody(SyntaxFactory.Block(SyntaxFactory.ParseStatement("return Boundary.Resolve();"))));
        string fixture = """
            using System; using System.Reflection; using System.Runtime.CompilerServices;
            using BepInEx; using BepInEx.Logging; using BepInEx.Bootstrap; using Redeploy.Drivers;
            [assembly: AssemblyMetadata("RedeployCoreBuiltAgainst", "1.0.8")]
            namespace BepInEx { public class BaseUnityPlugin { public bool enabled = true; public ManualLogSource Logger = new(); } }
            namespace BepInEx.Logging { public class ManualLogSource { public void LogError(string value) { Boundary.Errors++; } } }
            namespace BepInEx.Bootstrap { public static class Chainloader { public static System.Collections.Generic.List<string> DependencyErrors = new(); } }
            public static class Boundary {
                public static int Calls, Errors; public static string Installed;
                public static AssemblyName Resolve() => Installed == "missing" ? throw new System.IO.FileNotFoundException()
                    : new AssemblyName("maschine-Redeploy.Core, Version=" + Installed);
            }
            """;
        // Keep Plugin in its original namespace, but no unused game imports.
        string pluginSource = fixture + "\nnamespace " + (arena ? "ArenaMode" : "Redeploy") + " { " + plugin.ToFullString() + " }";
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("CoreBootstrap_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(pluginSource), CSharpSyntaxTree.ParseText(gateTree.ToFullString()) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output = new MemoryStream();
        var emit = compilation.Emit(output);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        var assembly = Assembly.Load(output.ToArray());
        var boundary = assembly.GetType("Boundary");
        boundary.GetField("Installed").SetValue(null, installed);
        var type = assembly.GetType((arena ? "ArenaMode" : "Redeploy") + ".Plugin");
        var instance = Activator.CreateInstance(type);
        foreach (string name in arena ? new[] { "Awake", "Start", "Update", "OnDestroy", "OnDestroy" } : new[] { "Awake" })
            type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(instance, null);
        Assert.Equal(accepted ? (arena ? 4 : 1) : 0, (int)boundary.GetField("Calls").GetValue(null));
        Assert.Equal(accepted ? 0 : 1, (int)boundary.GetField("Errors").GetValue(null));
        Assert.Equal(accepted, (bool)type.GetField("enabled").GetValue(instance));
        var errors = (System.Collections.IList)assembly.GetType("BepInEx.Bootstrap.Chainloader").GetField("DependencyErrors").GetValue(null);
        Assert.Equal(accepted ? 0 : 1, errors.Count);
    }
}
