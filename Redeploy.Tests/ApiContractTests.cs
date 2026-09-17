using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Xunit;

namespace Redeploy.Tests;

/// <summary>
/// The frozen RespawnApi v1 contract (docs/RESPAWN-API.md, sections 3.9 and 8.2):
/// the built Core matches api-v1.baseline.txt, the interop file only names members that
/// exist with the signature it binds to, and the compiled consumers installed next to the
/// game (ArenaMode.Client, Redeploy.Client) still resolve every Core member they reference.
/// The DLL-reading checks skip when the build or the game install is not there.
/// </summary>
public class ApiContractTests
{
    private const string CoreAssemblyName = "maschine-Redeploy.Core";
    private const string ApiTypeFullName = "Redeploy.Core.Api.RespawnApi";
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    internal static readonly string RepoRoot = FindRepoRoot();
    private static readonly string InstallRoot = Path.GetFullPath(Path.Combine(RepoRoot, "..", ".."));
    private static readonly string BaselinePath = Path.Combine(RepoRoot, "Redeploy.Core", "Api", "api-v1.baseline.txt");
    private static readonly string InteropPath = Path.Combine(RepoRoot, "interop", "RedeployRespawnInterop.cs");

    // ---------------------------------------------------------------- baseline

    [SkippableFact]
    public void BuiltCoreMatchesTheV1Baseline()
    {
        using var context = OpenCore(out var core);

        var expected = ReadBaseline();
        var actual = new SortedSet<string>(StringComparer.Ordinal);
        actual.UnionWith(Surface(core.GetType(ApiTypeFullName, throwOnError: true)));
        actual.UnionWith(Surface(core.GetType("Redeploy.Core.Engine.IDeathInterceptionDriver", throwOnError: true)));
        actual.UnionWith(Surface(core.GetType("Redeploy.Core.Spawning.ISpawnSelector", throwOnError: true)));

        var missing = expected.Except(actual).ToList();
        var added = actual.Except(expected).ToList();
        Assert.True(missing.Count == 0, "Baseline members missing from the built Core:\n" + string.Join("\n", missing));
        // Additive evolution means ApiVersion 2 and a new baseline; while it is 1 nothing may appear.
        Assert.True(added.Count == 0, "Members not in the v1 baseline (bump ApiVersion, add a baseline):\n" + string.Join("\n", added));
    }

    [SkippableFact]
    public void ApiVersionIsAPropertyNotALiteral()
    {
        using var context = OpenCore(out var core);
        var api = core.GetType(ApiTypeFullName, throwOnError: true);

        Assert.Null(api.GetField("ApiVersion", Declared));
        var property = api.GetProperty("ApiVersion", Declared);
        Assert.NotNull(property);
        Assert.Equal("System.Int32", Render(property.PropertyType));
    }

    // ---------------------------------------------------------------- interop drift

    [Fact]
    public void InteropFileBindsOnlyBaselineMembersWithMatchingSignatures()
    {
        var source = File.ReadAllText(InteropPath);
        var baseline = ReadBaseline();

        Assert.Contains("\"Redeploy.Core.Api.RespawnApi, maschine-Redeploy.Core\"", source);

        // Events are bound through Subscribe(api, "<event>", nameof(<handler>)): the handler's
        // parameter list must be exactly the delegate's, or CreateDelegate returns null at run time.
        var subscriptions = Regex.Matches(source, @"Subscribe\(api,\s*""(\w+)"",\s*nameof\((\w+)\)\)");
        Assert.NotEmpty(subscriptions);
        foreach (Match subscription in subscriptions)
        {
            var eventName = subscription.Groups[1].Value;
            var handler = subscription.Groups[2].Value;
            var signature = Regex.Match(source, @"private void " + handler + @"\(([^)]*)\)");
            Assert.True(signature.Success, $"handler {handler} not found in the interop file");
            var parameters = signature.Groups[1].Value.Trim();
            var delegateType = parameters.Length == 0
                ? "System.Action"
                : "System.Action`" + parameters.Split(',').Length + "[" + string.Join(",", parameters.Split(',').Select(CSharpTypeToFullName)) + "]";
            Assert.Contains($"RespawnApi event {eventName} {delegateType}", baseline);
        }

        foreach (Match getter in Regex.Matches(source, @"BoolGetter\(api,\s*""(\w+)""\)"))
            Assert.Contains($"RespawnApi property {getter.Groups[1].Value} System.Boolean get", baseline);

        Assert.Contains("RespawnApi property ApiVersion System.Int32 get", baseline);
        Assert.Matches(@"GetProperty\(""ApiVersion"",\s*PublicStatic\)", source);
        Assert.Matches(@"GetMethod\(""IsRespawnPending"",\s*PublicStatic,\s*null,\s*new\[\]\s*\{\s*typeof\(Player\)\s*\},\s*null\)", source);
        Assert.Contains("RespawnApi method IsRespawnPending System.Boolean(EFT.Player)", baseline);

        // Every string literal handed to the reflection lookups is accounted for above.
        var literals = Regex.Matches(source, @"(?:GetEvent\(|GetProperty\(|GetMethod\(|Subscribe\(api,|BoolGetter\(api,)\s*""(\w+)""")
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        foreach (var literal in literals)
            Assert.True(baseline.Any(line => line.StartsWith("RespawnApi ") && line.Split(' ')[2] == literal),
                $"interop file names '{literal}', which is not a v1 member");
    }

    // ---------------------------------------------------------------- old binaries

    [SkippableFact]
    public void InstalledConsumersStillResolveEveryCoreMemberTheyReference()
    {
        using var context = OpenCore(out var core);

        var consumers = new[]
        {
            Path.Combine(InstallRoot, "BepInEx", "plugins", "maschine-ArenaMode.Client.dll"),
            Path.Combine(InstallRoot, "BepInEx", "plugins", "maschine-Redeploy.Client.dll"),
        }.Where(File.Exists).ToList();
        Skip.If(consumers.Count == 0, "no compiled consumer installed next to the game");

        var problems = new List<string>();
        var checkedReferences = 0;
        foreach (var consumer in consumers)
        {
            using var stream = File.OpenRead(consumer);
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            var name = Path.GetFileName(consumer);

            foreach (var handle in metadata.TypeReferences)
            {
                var typeName = CoreTypeName(metadata, handle);
                if (typeName == null)
                    continue;
                checkedReferences++;
                if (core.GetType(typeName, throwOnError: false) == null)
                    problems.Add($"{name}: type {typeName} is gone");
            }

            foreach (var handle in metadata.MemberReferences)
            {
                var member = metadata.GetMemberReference(handle);
                if (member.Parent.Kind != HandleKind.TypeReference)
                    continue;
                var typeName = CoreTypeName(metadata, (TypeReferenceHandle)member.Parent);
                if (typeName == null)
                    continue;
                checkedReferences++;

                var type = core.GetType(typeName, throwOnError: false);
                if (type == null)
                    continue; // reported above

                var memberName = metadata.GetString(member.Name);
                var provider = new NameProvider();
                string wanted;
                bool found;
                if (member.GetKind() == MemberReferenceKind.Method)
                {
                    var signature = member.DecodeMethodSignature(provider, null);
                    wanted = RenderMethod(signature.ReturnType, signature.ParameterTypes, signature.Header.IsInstance);
                    found = type.GetMethods(Declared | BindingFlags.NonPublic)
                        .Concat(type.GetConstructors(Declared | BindingFlags.NonPublic).Cast<MethodBase>())
                        .Where(m => m.Name == memberName)
                        .Any(m => RenderMethod(m is MethodInfo info ? Render(info.ReturnType) : "System.Void",
                            m.GetParameters().Select(p => Render(p.ParameterType)).ToImmutableArray(), !m.IsStatic) == wanted);
                }
                else
                {
                    wanted = member.DecodeFieldSignature(provider, null);
                    var field = type.GetField(memberName, Declared | BindingFlags.NonPublic);
                    found = field != null && Render(field.FieldType) == wanted;
                }

                if (!found)
                    problems.Add($"{name}: {typeName}.{memberName} {wanted} no longer exists with that signature");
            }
        }

        // A scanner that finds nothing would pass for the wrong reason: both consumers
        // reference dozens of Core members (RespawnEngine, DeathInterception, RaidLifecycle).
        Assert.True(checkedReferences >= 20, $"only {checkedReferences} Core references found — the scanner is broken");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// The drivers and spawn selectors implement Core's interfaces implicitly: no MethodImpl
    /// rows, no MemberRefs, so a new interface member is invisible to the check above and
    /// would only surface as a TypeLoadException in the driver's Awake. Every interface an
    /// installed consumer implements must therefore still have exactly its baseline members.
    /// </summary>
    [SkippableFact]
    public void InterfacesTheInstalledConsumersImplementAreUnchanged()
    {
        using var context = OpenCore(out var core);
        var baseline = ReadBaseline();

        var consumers = new[]
        {
            Path.Combine(InstallRoot, "BepInEx", "plugins", "maschine-ArenaMode.Client.dll"),
            Path.Combine(InstallRoot, "BepInEx", "plugins", "maschine-Redeploy.Client.dll"),
        }.Where(File.Exists).ToList();
        Skip.If(consumers.Count == 0, "no compiled consumer installed next to the game");

        var implemented = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var consumer in consumers)
        {
            using var stream = File.OpenRead(consumer);
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            foreach (var typeHandle in metadata.TypeDefinitions)
            {
                var definition = metadata.GetTypeDefinition(typeHandle);
                foreach (var implHandle in definition.GetInterfaceImplementations())
                {
                    var impl = metadata.GetInterfaceImplementation(implHandle);
                    if (impl.Interface.Kind != HandleKind.TypeReference)
                        continue;
                    var interfaceName = CoreTypeName(metadata, (TypeReferenceHandle)impl.Interface);
                    if (interfaceName == null)
                        continue;
                    if (!implemented.TryGetValue(interfaceName, out var implementers))
                        implemented[interfaceName] = implementers = new List<string>();
                    implementers.Add($"{Path.GetFileName(consumer)}:{metadata.GetString(definition.Namespace)}.{metadata.GetString(definition.Name)}");
                }
            }
        }

        Assert.True(implemented.Count >= 2, "expected the drivers and spawn selectors to implement Core interfaces: " + string.Join(", ", implemented.Keys));
        foreach (var pair in implemented)
        {
            var type = core.GetType(pair.Key, throwOnError: false);
            Assert.True(type != null, $"{pair.Key} (implemented by {string.Join(", ", pair.Value)}) is gone from Core");
            var expected = baseline.Where(line => line.StartsWith(type.Name + " ")).ToList();
            Assert.True(expected.Count > 0, $"{pair.Key} is implemented by a consumer but has no baseline lines");
            var actual = Surface(type).OrderBy(l => l, StringComparer.Ordinal).ToList();
            Assert.True(expected.SequenceEqual(actual),
                $"{pair.Key} changed under its implementers ({string.Join(", ", pair.Value)}):\nexpected\n{string.Join("\n", expected)}\nactual\n{string.Join("\n", actual)}");
        }
    }

    // ---------------------------------------------------------------- helpers

    private static MetadataLoadContext OpenCore(out Assembly core)
    {
        var managed = Path.Combine(InstallRoot, "EscapeFromTarkov_Data", "Managed");
        var built = new[] { "Release", "Debug" }
            .Select(configuration => Path.Combine(RepoRoot, "Redeploy.Core", "bin", configuration, CoreAssemblyName + ".dll"))
            .FirstOrDefault(File.Exists);
        Skip.If(built == null, "maschine-Redeploy.Core.dll is not built (dotnet build Redeploy.Core -c Release)");
        Skip.If(!Directory.Exists(managed), "the game's Managed folder is not next to the repo");

        var paths = new List<string> { built };
        paths.AddRange(Directory.GetFiles(managed, "*.dll"));
        foreach (var extra in new[] { Path.Combine(InstallRoot, "BepInEx", "core"), Path.Combine(InstallRoot, "BepInEx", "plugins", "spt") })
            if (Directory.Exists(extra))
                paths.AddRange(Directory.GetFiles(extra, "*.dll"));

        var context = new MetadataLoadContext(new PathAssemblyResolver(paths), coreAssemblyName: "mscorlib");
        core = context.LoadFromAssemblyPath(built);
        return context;
    }

    private static SortedSet<string> ReadBaseline()
        => new(File.ReadAllLines(BaselinePath).Where(l => l.Length > 0 && !l.StartsWith("#")), StringComparer.Ordinal);

    /// <summary>One line per public member: "&lt;type&gt; &lt;kind&gt; &lt;name&gt; &lt;signature&gt;".</summary>
    private static IEnumerable<string> Surface(Type type)
    {
        var prefix = type.Name + " ";
        foreach (var field in type.GetFields(Declared))
            if (field.IsLiteral)
                yield return $"{prefix}const {field.Name} {Render(field.FieldType)} \"{field.GetRawConstantValue()}\"";
            else
                yield return $"{prefix}field {field.Name} {Render(field.FieldType)}";
        foreach (var property in type.GetProperties(Declared))
            yield return $"{prefix}property {property.Name} {Render(property.PropertyType)} {(property.CanRead ? "get" : "")}{(property.CanWrite ? " set" : "")}".TrimEnd();
        foreach (var evt in type.GetEvents(Declared))
            yield return $"{prefix}event {evt.Name} {Render(evt.EventHandlerType)}";
        foreach (var method in type.GetMethods(Declared))
            if (!method.IsSpecialName)
                yield return $"{prefix}method {method.Name} {Render(method.ReturnType)}({string.Join(",", method.GetParameters().Select(p => Render(p.ParameterType)))})";
    }

    private static string RenderMethod(string returnType, ImmutableArray<string> parameters, bool isInstance)
        => $"{(isInstance ? "instance " : "static ")}{returnType}({string.Join(",", parameters)})";

    private static string Render(Type type)
    {
        if (type.IsByRef) return Render(type.GetElementType()) + "&";
        if (type.IsPointer) return Render(type.GetElementType()) + "*";
        if (type.IsArray) return Render(type.GetElementType()) + "[]";
        if (type.IsGenericType && !type.IsGenericTypeDefinition)
            return type.GetGenericTypeDefinition().FullName + "[" + string.Join(",", type.GetGenericArguments().Select(Render)) + "]";
        return type.FullName ?? type.Name;
    }

    private static string CSharpTypeToFullName(string parameter)
    {
        var typeName = parameter.Trim().Split(' ')[0];
        return typeName switch
        {
            "string" => "System.String",
            "bool" => "System.Boolean",
            "int" => "System.Int32",
            "Player" => "EFT.Player",
            "LocalPlayer" => "EFT.LocalPlayer",
            "LocalGame" => "EFT.LocalGame",
            _ => typeName,
        };
    }

    /// <summary>Full name of a type reference into maschine-Redeploy.Core, or null for any other assembly.</summary>
    private static string CoreTypeName(MetadataReader metadata, TypeReferenceHandle handle)
    {
        var reference = metadata.GetTypeReference(handle);
        switch (reference.ResolutionScope.Kind)
        {
            case HandleKind.AssemblyReference:
                var assembly = metadata.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope);
                if (metadata.GetString(assembly.Name) != CoreAssemblyName)
                    return null;
                var ns = metadata.GetString(reference.Namespace);
                return (ns.Length == 0 ? "" : ns + ".") + metadata.GetString(reference.Name);
            case HandleKind.TypeReference:
                var outer = CoreTypeName(metadata, (TypeReferenceHandle)reference.ResolutionScope);
                return outer == null ? null : outer + "+" + metadata.GetString(reference.Name);
            default:
                return null;
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Redeploy.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Redeploy.sln not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Renders metadata signatures the way Render() renders reflection types.</summary>
    private sealed class NameProvider : ISignatureTypeProvider<string, object>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Boolean => "System.Boolean",
            PrimitiveTypeCode.Byte => "System.Byte",
            PrimitiveTypeCode.SByte => "System.SByte",
            PrimitiveTypeCode.Char => "System.Char",
            PrimitiveTypeCode.Int16 => "System.Int16",
            PrimitiveTypeCode.UInt16 => "System.UInt16",
            PrimitiveTypeCode.Int32 => "System.Int32",
            PrimitiveTypeCode.UInt32 => "System.UInt32",
            PrimitiveTypeCode.Int64 => "System.Int64",
            PrimitiveTypeCode.UInt64 => "System.UInt64",
            PrimitiveTypeCode.Single => "System.Single",
            PrimitiveTypeCode.Double => "System.Double",
            PrimitiveTypeCode.String => "System.String",
            PrimitiveTypeCode.Object => "System.Object",
            PrimitiveTypeCode.Void => "System.Void",
            PrimitiveTypeCode.IntPtr => "System.IntPtr",
            PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
            PrimitiveTypeCode.TypedReference => "System.TypedReference",
            _ => typeCode.ToString(),
        };

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var definition = reader.GetTypeDefinition(handle);
            var ns = reader.GetString(definition.Namespace);
            var name = reader.GetString(definition.Name);
            if (!definition.GetDeclaringType().IsNil)
                return GetTypeFromDefinition(reader, definition.GetDeclaringType(), rawTypeKind) + "+" + name;
            return (ns.Length == 0 ? "" : ns + ".") + name;
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var reference = reader.GetTypeReference(handle);
            var name = reader.GetString(reference.Name);
            if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
                return GetTypeFromReference(reader, (TypeReferenceHandle)reference.ResolutionScope, rawTypeKind) + "+" + name;
            var ns = reader.GetString(reference.Namespace);
            return (ns.Length == 0 ? "" : ns + ".") + name;
        }

        public string GetTypeFromSpecification(MetadataReader reader, object genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
            => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "[" + string.Join(",", typeArguments) + "]";
        public string GetGenericMethodParameter(object genericContext, int index) => "!!" + index;
        public string GetGenericTypeParameter(object genericContext, int index) => "!" + index;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => elementType;
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
    }
}
