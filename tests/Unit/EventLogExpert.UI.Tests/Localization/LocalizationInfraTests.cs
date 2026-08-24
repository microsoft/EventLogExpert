// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;

namespace EventLogExpert.UI.Tests.Localization;

/// <summary>
///     Culture-agnostic localization-infra tests (config guard, resolver, drift guard, no-pin guard);
///     culture-MUTATING tests live in <c>FindBarCultureTests</c>.
/// </summary>
public sealed class LocalizationInfraTests
{
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
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src");

        // Matches pins (=, ??=; not ==/!=), not reads: only assignments would break OS-culture-following.
        var pinPattern = new Regex(
            @"(CurrentCulture|CurrentUICulture|DefaultThreadCurrentCulture|DefaultThreadCurrentUICulture)\s*(\?\?)?=(?!=)",
            RegexOptions.Compiled);

        var offenders = Directory
            .EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
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

        var canary = CultureInfo.GetCultureInfo("qps-ploc").Name;
        var satelliteCultures = Directory.GetDirectories(AppContext.BaseDirectory)
            .Where(directory => File.Exists(Path.Combine(directory, "EventLogExpert.UI.resources.dll")))
            .Select(directory => CultureInfo.GetCultureInfo(Path.GetFileName(directory)).Name)
            .Where(name => !string.Equals(name, canary, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { neutral };
        expected.UnionWith(satelliteCultures);

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

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EventLogExpert.slnx"))) { return directory.FullName; }
        }

        throw new InvalidOperationException("Could not locate the repository root (EventLogExpert.slnx) from the test output.");
    }
}
