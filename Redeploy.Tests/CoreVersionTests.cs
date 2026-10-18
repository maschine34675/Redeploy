using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using Redeploy.Drivers;
using Xunit;

namespace Redeploy.Tests;

/// <summary>
/// Core's own version line and the driver skew check (docs/RESPAWN-API.md 3.9): the
/// comparison itself, and that the builds really carry what the check reads. The
/// build-reading checks skip when a project is not built.
/// </summary>
public class CoreVersionTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string CoreVersionProps = Path.Combine(RepoRoot, "Redeploy.Core", "CoreVersion.props");

    // ---------------------------------------------------------------- the comparison

    [Theory]
    [InlineData("0.9.0.0", "1.0.0")]
    [InlineData("1.0.0.0", "1.0.1")]
    [InlineData("1.0.0.0", "1.0.0.1")]
    [InlineData("1.2.0.0", "1.10.0")]
    public void AnOlderInstalledCoreIsReported(string installed, string builtAgainst)
    {
        var warning = CoreVersionGate.Evaluate("maschine-Test", Version.Parse(installed), builtAgainst);

        Assert.NotNull(warning);
        Assert.StartsWith("maschine-Test: ", warning);
        Assert.Contains(installed, warning);
        Assert.Contains("disabled", warning);
        Assert.Contains("keep only one copy", warning);
    }

    [Theory]
    [InlineData("1.0.0.0", "1.0.0")]    // an assembly version has four parts, the stamp three
    [InlineData("1.0.0.0", "1.0")]
    [InlineData("1.0.1.0", "1.0.0")]
    [InlineData("2.0.0.0", "1.9.9")]
    public void TheSameOrANewerCoreIsFine(string installed, string builtAgainst)
        => Assert.Null(CoreVersionGate.Evaluate("maschine-Test", Version.Parse(installed), builtAgainst));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    [InlineData("1")]
    public void AMissingOrBrokenStampFailsClosed(string builtAgainst)
        => Assert.Contains("disabled", CoreVersionGate.Evaluate("maschine-Test", new Version(0, 1, 0, 0), builtAgainst));

    [Fact]
    public void AnUnknownInstalledCoreFailsClosed()
        => Assert.Contains("disabled", CoreVersionGate.Evaluate("maschine-Test", null, "1.0.0"));

    [Fact]
    public void MissingUnloadableOrWrongAssemblyFailsClosed()
    {
        Assert.Contains("disabled", CoreVersionGate.Check("test", "1.0.8", () => throw new FileNotFoundException()));
        Assert.Contains("disabled", CoreVersionGate.Check("test", "1.0.8", () => throw new BadImageFormatException()));
        Assert.Contains("disabled", CoreVersionGate.Check("test", "1.0.8", () => null));
        Assert.Contains("disabled", CoreVersionGate.Check("test", "1.0.8", () => new AssemblyName("Foreign, Version=9.0.0.0")));
        Assert.Null(CoreVersionGate.Check("test", "1.0.8", () => new AssemblyName("maschine-Redeploy.Core, Version=1.0.8.0")));
        Assert.Null(CoreVersionGate.Check("test", "1.0.8", () => new AssemblyName("maschine-Redeploy.Core, Version=2.0.0.0")));
    }

    [Fact]
    public void MissingStampNeverResolvesCore()
    {
        bool resolved = false;
        Assert.Contains("disabled", CoreVersionGate.Check("test", null, () => { resolved = true; return null; }));
        Assert.False(resolved);
    }

    [Fact]
    public void TheStampIsReadFromAssemblyMetadata()
    {
        // This test assembly carries no stamp: the reader must answer null, not throw.
        Assert.Null(CoreVersionGate.ReadBuiltAgainst(typeof(CoreVersionTests).Assembly));
        Assert.Null(CoreVersionGate.ReadBuiltAgainst(null));
    }

    // ---------------------------------------------------------------- the builds

    [Fact]
    public void CoreVersionPropsDefinesTheVersionAndTheKeyTheGateReads()
    {
        var properties = ReadCoreVersionProps();

        Assert.True(Version.TryParse(properties["RedeployCoreVersion"], out _), "RedeployCoreVersion is not a version");
        Assert.Equal(CoreVersionGate.BuiltAgainstKey, properties["RedeployCoreBuiltAgainstKey"]);
    }

    [SkippableFact]
    public void BuiltCoreCarriesItsOwnVersionLine()
    {
        var built = FirstBuilt(Path.Combine(RepoRoot, "Redeploy.Core", "bin"), "maschine-Redeploy.Core.dll");
        Skip.If(built == null, "maschine-Redeploy.Core.dll is not built");

        var expected = Version.Parse(ReadCoreVersionProps()["RedeployCoreVersion"]);
        var actual = AssemblyName.GetAssemblyName(built).Version;

        Assert.Equal(new Version(expected.Major, expected.Minor, Math.Max(expected.Build, 0), 0), actual);
    }

    /// <summary>
    /// Redeploy.Client lives in this repo and is rebuilt with every Core bump: its stamp must equal
    /// CoreVersion.props. ArenaMode has its own release cadence: a build stamped with an older Core
    /// is fine against a newer one (the gate rejects an older installed Core), so its
    /// stamp only has to exist and must not be newer than this repo's Core.
    /// </summary>
    [SkippableTheory]
    [InlineData("Redeploy", "Redeploy.Client", "maschine-Redeploy.Client.dll", true)]
    [InlineData("ArenaMode", "ArenaMode.Client", "maschine-ArenaMode.Client.dll", false)]
    public void BuiltDriversAreStampedWithTheCoreVersion(string repo, string project, string fileName, bool sameRepo)
    {
        var projectBin = sameRepo
            ? Path.Combine(RepoRoot, project, "bin")
            : Path.Combine(RepoRoot, "..", repo, project, "bin");
        var built = FirstBuilt(projectBin, fileName);
        Skip.If(built == null, $"{fileName} is not built next to this repo");

        var stamp = ReadAssemblyMetadata(built, CoreVersionGate.BuiltAgainstKey);
        var core = ReadCoreVersionProps()["RedeployCoreVersion"];

        Assert.True(stamp != null, $"{fileName} has no {CoreVersionGate.BuiltAgainstKey} stamp; its Awake minimum-version check would reject startup");
        if (sameRepo)
        {
            Assert.Equal(core, stamp);
        }
        else
        {
            Assert.True(Version.TryParse(stamp, out var stamped), $"{fileName} stamp '{stamp}' is not a version");
            Assert.True(stamped <= Version.Parse(core),
                $"{fileName} is stamped {stamp}, newer than this repo's Core {core}: it would reject the Core built here");
        }
    }

    // ---------------------------------------------------------------- helpers

    private static Dictionary<string, string> ReadCoreVersionProps()
        => XDocument.Load(CoreVersionProps).Descendants("PropertyGroup").Elements()
            .ToDictionary(element => element.Name.LocalName, element => element.Value.Trim());

    private static string FirstBuilt(string binDirectory, string fileName)
        => new[] { "Release", "Debug" }
            .Select(configuration => Path.Combine(binDirectory, configuration, fileName))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

    /// <summary>Value of an assembly-level [AssemblyMetadata(key, value)], read as raw metadata.</summary>
    private static string ReadAssemblyMetadata(string path, string key)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();

        foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);
            if (AttributeTypeName(metadata, attribute) != "System.Reflection.AssemblyMetadataAttribute")
                continue;

            var value = attribute.DecodeValue(new StringOnlyProvider());
            if (value.FixedArguments.Length == 2 && (string)value.FixedArguments[0].Value == key)
                return (string)value.FixedArguments[1].Value;
        }

        return null;
    }

    private static string AttributeTypeName(MetadataReader metadata, CustomAttribute attribute)
    {
        if (attribute.Constructor.Kind != HandleKind.MemberReference)
            return null;
        var parent = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
        if (parent.Kind != HandleKind.TypeReference)
            return null;
        var type = metadata.GetTypeReference((TypeReferenceHandle)parent);
        return metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
    }

    private sealed class StringOnlyProvider : ICustomAttributeTypeProvider<Type>
    {
        public Type GetPrimitiveType(PrimitiveTypeCode typeCode)
            => typeCode == PrimitiveTypeCode.String ? typeof(string) : typeof(object);
        public Type GetSystemType() => typeof(Type);
        public Type GetSZArrayType(Type elementType) => elementType.MakeArrayType();
        public Type GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => typeof(object);
        public Type GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => typeof(object);
        public Type GetTypeFromSerializedName(string name) => typeof(object);
        public PrimitiveTypeCode GetUnderlyingEnumType(Type type) => PrimitiveTypeCode.Int32;
        public bool IsSystemType(Type type) => type == typeof(Type);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Redeploy.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Redeploy.sln not found above " + AppContext.BaseDirectory);
    }
}
