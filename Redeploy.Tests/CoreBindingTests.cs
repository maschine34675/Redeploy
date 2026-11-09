using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Redeploy.Tests;

/// <summary>
/// Runtime binding against the real built Core (docs/RESPAWN-API.md 8.2, last item). The contract
/// tests read Core as metadata only; here the unchanged interop file is compiled against the game's
/// own assemblies (as C# 7.3, the language level it promises), loaded next to the built Core, and its
/// TryBind runs for real: the assembly-qualified name resolves through a simple-name resolver like
/// BepInEx's LocalResolve, the four events get exactly one subscriber each, the three queries bind,
/// and OnDestroy leaves no subscriber behind. Skips when the Core build or the game is not there.
/// </summary>
public class CoreBindingTests
{
    private const string CoreAssemblyName = "maschine-Redeploy.Core";
    private static readonly string[] Events = { "Replaced", "DeathClaimed", "ReplacementFailed", "RaidEnded" };
    private static readonly string[] Queries = { "_isReplacing", "_isAvailable", "_isRespawnPending" };

    [SkippableFact]
    public void TheUnchangedInteropFileBindsToTheBuiltCoreAndLetsGoAgain()
    {
        var install = Path.GetFullPath(Environment.GetEnvironmentVariable("REDEPLOY_TEST_SPT_ROOT")
            ?? Path.Combine(ApiContractTests.RepoRoot, "..", ".."));
        var managed = Path.Combine(install, "EscapeFromTarkov_Data", "Managed");
        var core = new[] { "Release", "Debug" }
            .Select(configuration => Path.Combine(ApiContractTests.RepoRoot, "Redeploy.Core", "bin", configuration, CoreAssemblyName + ".dll"))
            .FirstOrDefault(File.Exists);
        Skip.If(core == null, "maschine-Redeploy.Core.dll is not built (dotnet build Redeploy.Core -c Release)");
        Skip.If(!Directory.Exists(managed), "the game's Managed folder is not next to the repo");

        var interop = CompileInterop(managed);
        var context = new InstallContext(core, managed, Path.Combine(install, "BepInEx", "core"), Path.Combine(install, "BepInEx", "plugins", "spt"));
        try
        {
            using (context.EnterContextualReflection())
            {
                var trackerType = context.LoadFromStream(new MemoryStream(interop)).GetType("YourMod.Interop.LocalBodyTracker", throwOnError: true);
                var api = context.LoadFromAssemblyName(new AssemblyName(CoreAssemblyName)).GetType("Redeploy.Core.Api.RespawnApi", throwOnError: true);
                Assert.Equal(Path.GetFullPath(core), Path.GetFullPath(api.Assembly.Location));

                var log = new List<string>();
                var tracker = Activator.CreateInstance(trackerType, nonPublic: true);
                trackerType.GetField("Log").SetValue(tracker, (Action<string>)log.Add);

                Invoke(tracker, "TryBind");

                Assert.True((bool)trackerType.GetProperty("IsBound").GetValue(tracker), "not bound; tracker log:\n" + string.Join("\n", log));
                Assert.Contains($"bound to RespawnApi v1 (interop revision {InteropRevision()})", log);
                foreach (var name in Events)
                    Assert.True(Subscribers(api, name) == 1, $"{name} has {Subscribers(api, name)} subscribers after TryBind, expected 1");
                foreach (var name in Queries)
                    Assert.True(Field(tracker, name) != null, $"query {name} did not bind");

                Invoke(tracker, "OnDestroy");

                Assert.False((bool)trackerType.GetProperty("IsBound").GetValue(tracker));
                foreach (var name in Events)
                    Assert.True(Subscribers(api, name) == 0, $"{name} still has {Subscribers(api, name)} subscribers after OnDestroy");
                foreach (var name in Queries)
                    Assert.True(Field(tracker, name) == null, $"query {name} is still bound after OnDestroy");
            }
        }
        finally
        {
            context.Unload();
        }
    }

    /// <summary>
    /// The interop file states its revision three times: the header comment a reader sees in
    /// every copy, the Revision constant and (through it) the bind log line. A change that bumps
    /// one and forgets the other would make bug reports point at the wrong copy.
    /// </summary>
    [Fact]
    public void TheInteropHeaderAndTheRevisionConstantAgree()
    {
        var source = File.ReadAllText(Path.Combine(ApiContractTests.RepoRoot, "interop", "RedeployRespawnInterop.cs"));
        var header = Regex.Match(source, @"^// RedeployRespawnInterop\.cs - interop revision (\d+) for maschine-Redeploy\.Core", RegexOptions.Multiline);
        Assert.True(header.Success, "the header line naming the interop revision is missing");
        Assert.Equal(InteropRevision(), int.Parse(header.Groups[1].Value));
        Assert.Contains("// SPDX-License-Identifier: MIT", source);
        Assert.Contains("https://github.com/maschine34675/Redeploy/blob/main/interop/RedeployRespawnInterop.cs", source);
        Assert.Contains("\"bound to RespawnApi v\" + version.GetValue(null, null) + \" (interop revision \" + Revision + \")\"", source);
    }

    private static int InteropRevision()
    {
        var source = File.ReadAllText(Path.Combine(ApiContractTests.RepoRoot, "interop", "RedeployRespawnInterop.cs"));
        var constant = Regex.Match(source, @"public const int Revision = (\d+);");
        Assert.True(constant.Success, "LocalBodyTracker.Revision is missing from the interop file");
        return int.Parse(constant.Groups[1].Value);
    }

    /// <summary>
    /// The consumer example (interop/examples) compiles together with the unchanged interop file
    /// against the game's assemblies and BepInEx, as C# 7.3: the documentation's starting point
    /// cannot rot unnoticed.
    /// </summary>
    [SkippableFact]
    public void TheConsumerExampleCompilesWithTheInteropFileAgainstTheGame()
    {
        var install = Path.GetFullPath(Environment.GetEnvironmentVariable("REDEPLOY_TEST_SPT_ROOT")
            ?? Path.Combine(ApiContractTests.RepoRoot, "..", ".."));
        var managed = Path.Combine(install, "EscapeFromTarkov_Data", "Managed");
        var bepinex = Path.Combine(install, "BepInEx", "core", "BepInEx.dll");
        Skip.If(!Directory.Exists(managed), "the game's Managed folder is not next to the repo");
        Skip.If(!File.Exists(bepinex), "BepInEx/core/BepInEx.dll is not next to the repo");

        var options = new CSharpParseOptions(LanguageVersion.CSharp7_3);
        var sources = new[]
        {
            Path.Combine(ApiContractTests.RepoRoot, "interop", "RedeployRespawnInterop.cs"),
            Path.Combine(ApiContractTests.RepoRoot, "interop", "examples", "RespawnConsumerExample.cs"),
        }.Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), options, path));
        var references = Directory.GetFiles(managed, "*.dll").Where(HasMetadata).Append(bepinex)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "RespawnConsumerExample", sources, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        var errors = result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(result.Success, "the consumer example does not compile against the game:\n" + string.Join("\n", errors));
    }

    /// <summary>
    /// Every C# snippet in docs/RECIPES.md and docs/SOFT-DEPENDENCY.md that declares a whole type
    /// compiles, each on its own, together with the unchanged interop file against the game,
    /// BepInEx and Harmony, as C# 7.3. A snippet without a namespace gets the usings a reader
    /// would add and a namespace of its own. Fragments (a handler, a few statements) are left to
    /// reading; the count guard notices when the selection stops finding the snippets.
    /// </summary>
    [SkippableFact]
    public void TheCompleteSnippetsInTheConsumerDocsCompile()
    {
        var install = Path.GetFullPath(Environment.GetEnvironmentVariable("REDEPLOY_TEST_SPT_ROOT")
            ?? Path.Combine(ApiContractTests.RepoRoot, "..", ".."));
        var managed = Path.Combine(install, "EscapeFromTarkov_Data", "Managed");
        var bepinexCore = Path.Combine(install, "BepInEx", "core");
        var bepinex = Path.Combine(bepinexCore, "BepInEx.dll");
        var harmony = Path.Combine(bepinexCore, "0Harmony.dll");
        Skip.If(!Directory.Exists(managed), "the game's Managed folder is not next to the repo");
        Skip.If(!File.Exists(bepinex) || !File.Exists(harmony), "BepInEx/core (BepInEx.dll, 0Harmony.dll) is not next to the repo");

        var options = new CSharpParseOptions(LanguageVersion.CSharp7_3);
        var interopPath = Path.Combine(ApiContractTests.RepoRoot, "interop", "RedeployRespawnInterop.cs");
        var interop = CSharpSyntaxTree.ParseText(File.ReadAllText(interopPath), options, interopPath);
        var references = Directory.GetFiles(managed, "*.dll").Where(HasMetadata).Append(bepinex).Append(harmony)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToList();

        var snippets = new[] { "RECIPES.md", "SOFT-DEPENDENCY.md" }
            .SelectMany(doc => CompleteSnippets(Path.Combine(ApiContractTests.RepoRoot, "docs", doc))
                .Select(snippet => (Doc: doc, snippet.Line, snippet.Code)))
            .ToList();
        Assert.True(snippets.Count >= 8,
            $"found only {snippets.Count} complete snippets (" + string.Join(", ", snippets.Select(s => s.Doc + ":" + s.Line))
            + "); did the fences or the selection rule change?");

        var failures = new List<string>();
        foreach (var (doc, line, code) in snippets)
        {
            var tree = CSharpSyntaxTree.ParseText(code, options, $"{doc}:{line}");
            var compilation = CSharpCompilation.Create(
                "Snippet", new[] { interop, tree }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var output = new MemoryStream();
            var errors = compilation.Emit(output).Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
            if (errors.Count > 0)
                failures.Add($"docs/{doc}, snippet at line {line}:\n  " + string.Join("\n  ", errors));
        }
        Assert.True(failures.Count == 0, "consumer-doc snippets that do not compile:\n" + string.Join("\n", failures));
    }

    private const string SnippetUsings =
        "using System;\nusing System.Collections;\nusing System.Collections.Generic;\nusing Comfort.Common;\nusing EFT;\nusing UnityEngine;\nusing YourMod.Interop;\n";

    /// <summary>
    /// The csharp blocks of a document that declare a type and are written as a complete file:
    /// after leading comment lines they start with a using, a namespace, an attribute or a type
    /// declaration. Blocks that start with a member (a field, a handler) are fragments.
    /// </summary>
    private static IEnumerable<(int Line, string Code)> CompleteSnippets(string path)
    {
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        foreach (Match block in Regex.Matches(text, "```csharp\n(.*?)```", RegexOptions.Singleline))
        {
            var code = block.Groups[1].Value;
            if (!Regex.IsMatch(code, @"^[ \t]*(?:(?:public|internal|private)[ \t]+)?(?:(?:sealed|static|abstract|partial)[ \t]+)*(?:class|struct)[ \t]", RegexOptions.Multiline))
                continue;
            var start = Regex.Replace(code, @"^(?:[ \t]*(?://[^\n]*)?\n)*", "").TrimStart();
            if (!Regex.IsMatch(start, @"^(?:using\b|namespace\b|\[|(?:public|internal|sealed|static|abstract|partial|class|struct)\b)"))
                continue;
            var line = text.Take(block.Index).Count(c => c == '\n') + 1;
            if (!code.Contains("namespace "))
            {
                var usings = string.Concat(Regex.Matches(code, @"^[ \t]*using [\w.]+;[ \t]*\n", RegexOptions.Multiline).Select(m => m.Value.TrimStart()));
                var body = Regex.Replace(code, @"^[ \t]*using [\w.]+;[ \t]*\n", "", RegexOptions.Multiline);
                code = SnippetUsings + usings + $"namespace Snippet{line}\n{{\n{body}\n}}\n";
            }
            yield return (line, code);
        }
    }

    /// <summary>The interop file exactly as shipped, compiled like a consumer mod compiles it.</summary>
    private static byte[] CompileInterop(string managed)
    {
        var source = File.ReadAllText(Path.Combine(ApiContractTests.RepoRoot, "interop", "RedeployRespawnInterop.cs"));
        var references = Directory.GetFiles(managed, "*.dll").Where(HasMetadata).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "RespawnInteropBinding",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp7_3)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        var errors = result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(result.Success, "the interop file does not compile against the game:\n" + string.Join("\n", errors));
        return output.ToArray();
    }

    private static bool HasMetadata(string path)
    {
        try
        {
            using var reader = new PEReader(File.OpenRead(path));
            return reader.HasMetadata;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    private static void Invoke(object target, string method)
        => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

    /// <summary>The query delegates are private to the interop file; a rename there should say so, not throw a NullReferenceException.</summary>
    private static object Field(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.True(field != null, $"LocalBodyTracker has no field {name} any more; the interop file was refactored, update Queries in this test");
        return field.GetValue(target);
    }

    /// <summary>Field-like events keep their delegate in a private static field of the same name.</summary>
    private static int Subscribers(Type api, string eventName)
        => api.GetField(eventName, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) is Delegate handlers
            ? handlers.GetInvocationList().Length
            : 0;

    /// <summary>
    /// Framework assemblies come from the test host; everything else is found by simple name, the way
    /// BepInEx's LocalResolve finds it: the built Core first, then Managed, BepInEx/core and plugins/spt.
    /// </summary>
    private sealed class InstallContext : AssemblyLoadContext
    {
        private readonly Dictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);

        public InstallContext(string core, params string[] directories) : base("RedeployCoreBinding", isCollectible: true)
        {
            _paths[CoreAssemblyName] = core;
            foreach (var directory in directories.Where(Directory.Exists))
                foreach (var path in Directory.GetFiles(directory, "*.dll"))
                    _paths.TryAdd(Path.GetFileNameWithoutExtension(path), path);

            Resolving += (context, name) => _paths.TryGetValue(name.Name, out var path) ? context.LoadFromAssemblyPath(path) : null;
        }

        protected override Assembly Load(AssemblyName name) => null;
    }
}
