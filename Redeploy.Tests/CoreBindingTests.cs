using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
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
        var install = Path.GetFullPath(Path.Combine(ApiContractTests.RepoRoot, "..", ".."));
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
                Assert.Contains("bound to RespawnApi v1", log);
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
