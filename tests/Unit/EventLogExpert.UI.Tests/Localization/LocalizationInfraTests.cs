// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.Scenarios.Catalog;
using EventLogExpert.UI.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EventLogExpert.UI.Tests.Localization;

/// <summary>
///     Culture-agnostic localization-infra tests (config guard, resolver, drift guard, no-pin guard);
///     culture-MUTATING tests live in <c>FindBarCultureTests</c>.
/// </summary>
public sealed class LocalizationInfraTests
{
    [Fact]
    public void DynamicKeyFamilies_MatchEnumMembersExactly()
    {
        // Bidirectional per family: an authored key with no enum member is an orphan/typo; an enum member with no
        // key echoes its key name at runtime (Localizer[$"{stem}{member}"]). The two-rule orphan guard cannot see
        // either case because the family's interpolation site keeps the whole stem "referenced".
        (string Stem, Type EnumType)[] families =
        [
            ("Settings_Theme_", typeof(Theme)),
            ("Settings_CopyFormat_", typeof(EventCopyFormat)),
            ("Settings_LogLevel_", typeof(LogLevel)),
            ("Dashboard_Group_", typeof(ScenarioGroup))
        ];

        var resxKeys = ResxKeys();

        foreach (var (stem, enumType) in families)
        {
            var expected = Enum.GetNames(enumType)
                .Select(name => stem + name)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();

            var actual = resxKeys
                .Where(key => key.StartsWith(stem, StringComparison.Ordinal))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void EveryResourceKey_IsReferencedInProductionSource()
    {
        // A key is referenced if its literal "{key}" appears in source, OR it belongs to a documented dynamic
        // family whose interpolation site ($"{stem}...) is present - so deleting that site re-surfaces the whole
        // family as orphaned. obj/bin are excluded (via LocalizationSourceScan) so a stale generated .g.cs can
        // never keep a truly-orphaned key green. Individual dynamic-member orphans are caught by the enum cross-check below.
        string[] dynamicStems = ["Dashboard_Group_", "Settings_CopyFormat_", "Settings_LogLevel_", "Settings_Theme_"];

        var source = string.Join("\n", LocalizationSourceScan.EnumerateProductionSource().Select(File.ReadAllText));

        var orphans = ResxKeys()
            .Where(key =>
                !source.Contains($"\"{key}\"", StringComparison.Ordinal) &&
                !dynamicStems.Any(stem =>
                    key.StartsWith(stem, StringComparison.Ordinal) &&
                    source.Contains($"$\"{stem}", StringComparison.Ordinal)))
            .ToList();

        Assert.True(orphans.Count == 0, $"Authored-but-unreferenced RESX keys: {string.Join(", ", orphans)}");
    }

    [Fact]
    public void Localizer_KnownKey_ResolvesToNeutralValue()
    {
        var result = BuildLocalizer()["FindBar_NoResults"];

        Assert.False(result.ResourceNotFound);
        Assert.Equal("No results", result.Value);
    }

    [Fact]
    public void Localizer_MissingKey_ReportsNotFoundAndEchoesKey()
    {
        var result = BuildLocalizer()["FindBar_ThisKeyDoesNotExist"];

        Assert.True(result.ResourceNotFound);
        Assert.Equal("FindBar_ThisKeyDoesNotExist", result.Value);
    }

    [Fact]
    public void ProductionSource_NeverPinsThreadCulture()
    {
        // Matches pins (=, ??=; not ==/!=), not reads: only assignments would break OS-culture-following.
        var pinPattern = new Regex(
            @"(CurrentCulture|CurrentUICulture|DefaultThreadCurrentCulture|DefaultThreadCurrentUICulture)\s*(\?\?)?=(?!=)",
            RegexOptions.Compiled);

        var sourceRoot = Path.Combine(LocalizationSourceScan.RepositoryRoot, "src");
        var offenders = LocalizationSourceScan.EnumerateProductionSource()
            .Where(path => pinPattern.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(sourceRoot, path))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Production code must not pin thread culture (it follows the OS). Offending files: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Resolve_InvariantCulture_TerminatesAtNeutral()
    {
        var resolved = ContentCulture.Resolve(CultureInfo.InvariantCulture, ContentCulture.SupportedUiCultures);

        Assert.Equal("en", resolved.Name);
        Assert.Equal("ltr", ContentCulture.DirectionOf(resolved));
    }

    [Fact]
    public void Resolve_SupportedRtlCulture_ReturnsThatCultureRtl()
    {
        // Once a culture is in the supported set, its own direction is honored.
        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "en", "ar" };

        var resolved = ContentCulture.Resolve(CultureInfo.GetCultureInfo("ar-SA"), supported);

        Assert.Equal("ar", resolved.Name);
        Assert.Equal("rtl", ContentCulture.DirectionOf(resolved));
    }

    [Theory]
    [InlineData("en-US", "en", "ltr")]
    [InlineData("en-GB", "en", "ltr")]
    [InlineData("en", "en", "ltr")]
    [InlineData("ar-SA", "en", "ltr")] // unsupported RTL OS -> neutral en/ltr today (no regression)
    [InlineData("zh-Hans-CN", "en", "ltr")]
    public void Resolve_UnsupportedCulture_FallsBackToNeutralLtr(string current, string expectedName, string expectedDir)
    {
        var resolved = ContentCulture.Resolve(
            CultureInfo.GetCultureInfo(current),
            ContentCulture.SupportedUiCultures);

        Assert.Equal(expectedName, resolved.Name);
        Assert.Equal(expectedDir, ContentCulture.DirectionOf(resolved));
    }

    [Fact]
    public void SupportedUiCultures_MatchesEmbeddedSatellites_ExcludingCanary()
    {
        // Neutral comes from the assembly's [NeutralResourcesLanguage] so changing/removing it fails this guard, not silently passes.
        var neutralAttribute = typeof(SharedResource).Assembly.GetCustomAttribute<NeutralResourcesLanguageAttribute>();
        Assert.NotNull(neutralAttribute);
        var neutral = CultureInfo.GetCultureInfo(neutralAttribute.CultureName).Name;

        // Derive the satellite filename from the catalog assembly so this guard follows the resource assembly if it is ever relocated again.
        var satelliteFileName = typeof(SharedResource).Assembly.GetName().Name + ".resources.dll";
        var satelliteCultures = Directory.GetDirectories(AppContext.BaseDirectory)
            .Where(directory => File.Exists(Path.Combine(directory, satelliteFileName)))
            .Select(directory => CultureInfo.GetCultureInfo(Path.GetFileName(directory)).Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Positive control: the qps-ploc canary satellite must be discovered, so a zero-satellite result (e.g. a renamed resource assembly) fails loudly here instead of passing this guard vacuously.
        var canary = CultureInfo.GetCultureInfo("qps-ploc").Name;
        Assert.True(
            satelliteCultures.Contains(canary),
            $"No '{canary}' satellite ('{satelliteFileName}') was discovered under '{AppContext.BaseDirectory}'. " +
            "The satellite drift-guard cannot function without it; verify the resource assembly name and packaging.");

        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { neutral };
        expected.UnionWith(satelliteCultures.Where(name => !string.Equals(name, canary, StringComparison.OrdinalIgnoreCase)));

        Assert.True(
            ContentCulture.SupportedUiCultures.SetEquals(expected),
            $"SupportedUiCultures [{string.Join(", ", ContentCulture.SupportedUiCultures)}] must equal " +
            $"neutral-plus-non-canary-satellites [{string.Join(", ", expected)}]. Ship a translation's culture here " +
            "ONLY after the RTL prerequisite bundle lands.");
    }

    // Resolves the localizer from a bare container: the production extension plus the ILoggerFactory it needs (as the host supplies in production).
    private static IStringLocalizer<SharedResource> BuildLocalizer() =>
        new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddEventLogLocalization()
            .BuildServiceProvider()
            .GetRequiredService<IStringLocalizer<SharedResource>>();

    private static IReadOnlyList<string> ResxKeys() =>
        XDocument.Load(LocalizationSourceScan.ResxPath)
            .Root!.Elements("data")
            .Select(data => (string?)data.Attribute("name"))
            .Where(name => name is not null)
            .Select(name => name!)
            .ToList();
}
