using System.Xml.Linq;

namespace HardwareVision.Tests;

internal static class ReleaseReadinessTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests() =>
    [
        ("Release contract 01 Version is 2.0.1", () => PropertyIs("Version", "2.0.1")),
        ("Release contract 02 AssemblyVersion is 2.0.1.0", () => PropertyIs("AssemblyVersion", "2.0.1.0")),
        ("Release contract 03 FileVersion is 2.0.1.0", () => PropertyIs("FileVersion", "2.0.1.0")),
        ("Release contract 04 InformationalVersion is 2.0.1", () => PropertyIs("InformationalVersion", "2.0.1")),
        ("Release contract 05 version has no preview suffix", StableVersion),
        ("Release contract 06 target framework is net8 Windows", () => PropertyIs("TargetFramework", "net8.0-windows")),
        ("Release contract 07 runtime is win-x64", () => PropertyIs("RuntimeIdentifier", "win-x64")),
        ("Release contract 08 platform is x64", () => PropertyIs("PlatformTarget", "x64")),
        ("Release contract 09 manifest requires administrator", ManifestRequiresAdministrator),
        ("Release contract 10 PresentMon executable is embedded", () => ProjectContains("ThirdParty\\PresentMon\\2.5.1\\PresentMon.exe")),
        ("Release contract 11 PresentMon license is embedded", () => ProjectContains("ThirdParty\\PresentMon\\2.5.1\\LICENSE.txt")),
        ("Release contract 12 PresentMon notices are embedded", () => ProjectContains("ThirdParty\\PresentMon\\2.5.1\\THIRD_PARTY.txt")),
        ("Release contract 13 Package supports dispatch", () => PackageContains("workflow_dispatch:")),
        ("Release contract 14 Package supports v tags", () => PackageContains("tags:", "- \"v*\"")),
        ("Release contract 15 tag must match Version", () => PackageContains("$expectedTag = \"v$productVersion\"", "must exactly match")),
        ("Release contract 16 tag build rejects unstable suffix", () => PackageContains("dev|preview|alpha|beta|rc")),
        ("Release contract 17 publish is framework dependent", () => PackageContains("--self-contained false")),
        ("Release contract 18 publish is single file", () => PackageContains("PublishSingleFile=true")),
        ("Release contract 19 publish is untrimmed", () => PackageContains("PublishTrimmed=false")),
        ("Release contract 20 native libraries self extract", () => PackageContains("IncludeNativeLibrariesForSelfExtract=true")),
        ("Release contract 21 output is one EXE", () => PackageContains("Release output must contain exactly one HardwareVision.exe")),
        ("Release contract 22 output is HardwareVision.exe", () => PackageContains("Name -cne \"HardwareVision.exe\"")),
        ("Release contract 23 PE x64 is verified", () => PackageContains("$machine -ne 0x8664")),
        ("Release contract 24 ProductVersion is verified", () => PackageContains("ProductVersion -cne $env:PRODUCT_VERSION")),
        ("Release contract 25 FileVersion is verified", () => PackageContains("FileVersion -cne $env:FILE_VERSION")),
        ("Release contract 26 signing or unsigned is explicit", () => PackageContains("SIGNED:", "UNSIGNED:")),
        ("Release contract 27 signing adds no sidecar", () => PackageContains("Signing must not add release sidecar files")),
        ("Release contract 28 internal SHA256 is retained", () => PackageContains("SHA256=")),
        ("Release contract 29 package checks clean tree", () => PackageContains("Package build modified the repository working tree")),
        ("Release contract 30 release artifact only uploads EXE", ReleaseArtifactOnlyUploadsExe),
        ("Release contract 31 no ZIP asset", () => PackageExcludes("Compress-Archive", ".zip")),
        ("Release contract 32 no checksum asset", () => UploadSectionExcludes("SHA256SUMS")),
        ("Release contract 33 no build-info asset", () => UploadSectionExcludes("build-info.txt")),
        ("Release contract 34 no signing-status asset", () => UploadSectionExcludes("signing-status.txt")),
        ("Release contract 35 CI checks source hygiene", () => CiContains("git diff --check", "Tracked text files contain trailing whitespace")),
        ("Release contract 36 CI checks clean worktree", () => CiContains("Build or tests modified the repository working tree")),
        ("Release contract 37 schema version is untouched", SchemaVersionUntouched),
        ("Release contract 38 project dependencies remain four", ProjectDependencyCount),
        ("Release contract 39 package has no guessed inputs", PackageHasNoInputs),
        ("Release contract 40 release title contract", () => TestSupport.Equal("HardwareVision v2.0.1", $"HardwareVision v{Property("Version")}", "release title"))
    ];

    private static string Read(params string[] parts) => TraceworkPilotSource.Read(parts);
    private static string Project => Read("HardwareVision", "HardwareVision.csproj");
    private static string Package => Read(".github", "workflows", "package.yml");
    private static string Ci => Read(".github", "workflows", "ci.yml");
    private static string Manifest => Read("HardwareVision", "app.manifest");

    private static string Property(string name)
    {
        XDocument project = XDocument.Parse(Project);
        return project.Descendants(name).Select(element => element.Value).FirstOrDefault() ?? string.Empty;
    }

    private static void PropertyIs(string name, string expected) =>
        TestSupport.Equal(expected, Property(name), name);

    private static void StableVersion()
    {
        string version = Property("Version");
        foreach (string suffix in new[] { "dev", "preview", "alpha", "beta", "rc" })
        {
            TestSupport.False(version.Contains(suffix, StringComparison.OrdinalIgnoreCase), suffix);
        }
    }

    private static void ManifestRequiresAdministrator() =>
        TestSupport.True(Manifest.Contains("requestedExecutionLevel level=\"requireAdministrator\"", StringComparison.Ordinal), "requireAdministrator");

    private static void ProjectContains(params string[] values) => Contains(Project, values);
    private static void PackageContains(params string[] values) => Contains(Package, values);
    private static void CiContains(params string[] values) => Contains(Ci, values);
    private static void PackageExcludes(params string[] values) => Excludes(Package, values);

    private static void Contains(string source, params string[] values)
    {
        foreach (string value in values) TestSupport.True(source.Contains(value, StringComparison.Ordinal), value);
    }

    private static void Excludes(string source, params string[] values)
    {
        foreach (string value in values) TestSupport.False(source.Contains(value, StringComparison.OrdinalIgnoreCase), value);
    }

    private static string ReleaseUploadSection()
    {
        int start = Package.IndexOf("- name: Upload release executable", StringComparison.Ordinal);
        int end = Package.IndexOf("- name: Upload package logs", start, StringComparison.Ordinal);
        return Package[start..end];
    }

    private static void ReleaseArtifactOnlyUploadsExe()
    {
        string upload = ReleaseUploadSection();
        Contains(upload, "hardwarevision-release-executable", "HardwareVision.exe");
    }

    private static void UploadSectionExcludes(string value) => Excludes(ReleaseUploadSection(), value);

    private static void SchemaVersionUntouched()
    {
        string recorder = Read("HardwareVision", "Services", "CsvGameSessionRecorder.cs");
        TestSupport.True(recorder.Contains("SessionSchemaVersion = 4", StringComparison.Ordinal), "summary schema v4");
    }

    private static void ProjectDependencyCount()
    {
        XDocument project = XDocument.Parse(Project);
        TestSupport.Equal(4, project.Descendants("PackageReference").Count(), "PackageReference count");
    }

    private static void PackageHasNoInputs()
    {
        int dispatch = Package.IndexOf("workflow_dispatch:", StringComparison.Ordinal);
        int push = Package.IndexOf("push:", dispatch, StringComparison.Ordinal);
        string dispatchBlock = Package[dispatch..push];
        TestSupport.False(dispatchBlock.Contains("inputs:", StringComparison.Ordinal), "package inputs");
    }
}
