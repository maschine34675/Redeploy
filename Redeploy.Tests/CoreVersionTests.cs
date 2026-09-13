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
        Assert.Contains("reinstall the latest Redeploy or ArenaMode", warning);
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
    public void AMissingOrBrokenStampFailsOpen(string builtAgainst)
        => Assert.Null(CoreVersionGate.Evaluate("maschine-Test", new Version(0, 1, 0, 0), builtAgainst));

    [Fact]
    public void AnUnknownInstalledCoreFailsOpen()
        => Assert.Null(CoreVersionGate.Evaluate("maschine-Test", null, "1.0.0"));

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

    [SkippableTheory]
    [InlineData("Redeploy", "Redeploy.Client", "maschine-Redeploy.Client.dll")]
    [InlineData("ArenaMode", "ArenaMode.Client", "maschine-ArenaMode.Client.dll")]
    public void BuiltDriversAreStampedWithTheCoreVersion(string repo, string project, string fileName)
    {
        var projectBin = Path.Combine(RepoRoot, "..", repo, project, "bin");
        var built = FirstBuilt(projectBin, fileName);
        Skip.If(built == null, $"{fileName} is not built next to this repo");

        var stamp = ReadAssemblyMetadata(built, CoreVersionGate.BuiltAgainstKey);

        Assert.True(stamp != null, $"{fileName} has no {CoreVersionGate.BuiltAgainstKey} stamp; its Awake skew check would never fire");
        Assert.Equal(ReadCoreVersionProps()["RedeployCoreVersion"], stamp);
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
