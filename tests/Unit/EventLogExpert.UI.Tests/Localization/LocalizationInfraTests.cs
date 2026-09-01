// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.ActivityCorrelation;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Display;
using EventLogExpert.Runtime.DetailsPane;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Memory;
using EventLogExpert.Runtime.ResolutionCoverage;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.Runtime.Stats;
using EventLogExpert.Runtime.StatusBar;
using EventLogExpert.Scenarios.Catalog;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.FilterEditor.Comparison;
using EventLogExpert.UI.Globalization;
using EventLogExpert.UI.Modal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Collections.Immutable;
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
    public void EnumMappedKeyFamilies_MatchEnumMembersExactly()
    {
        // Bidirectional per family: an authored key with no enum member is an orphan/typo; an enum member with no
        // key echoes its key name at runtime (Localizer[$"{stem}{member}"]). The two-rule orphan guard cannot see
        // either case because the family's interpolation site keeps the whole stem "referenced".
        (string Stem, Type EnumType)[] families =
        [
            ("Settings_Theme_", typeof(Theme)),
            ("Settings_CopyFormat_", typeof(EventCopyFormat)),
            ("Settings_LogLevel_", typeof(LogLevel)),
            ("Dashboard_Group_", typeof(ScenarioGroup)),
            ("Dashboard_Presence_", typeof(ChannelPresence)),
            ("Dashboard_Enablement_", typeof(ChannelEnablement)),
            ("Details_Placeholder_", typeof(PlaceholderKind)),
            ("Details_Property_", typeof(DetailsPropertyLabel)),
            ("Explain_", typeof(GlossaryTerm)),
            ("ResolutionStatus_", typeof(EventResolutionStatus)),
            ("Correlation_Role_", typeof(ActivityNodeRole)),
            ("Coverage_Status_", typeof(CoverageStatus)),
            ("Severity_Level_", typeof(SeverityLevel)),
            ("Histogram_Dimension_", typeof(HistogramDimension)),
            ("Histogram_HighlightColor_", typeof(HighlightColor)),
            ("Histogram_Severity_", typeof(HistogramSeverityBucket)),
            ("Stats_Dimension_", typeof(StatsDimension)),
            ("StatusBar_Memory_Value_", typeof(MemoryUsageLevel)),
            ("StatusBar_Memory_Announce_", typeof(MemoryUsageLevel)),
            ("StatusBar_Memory_Tooltip_", typeof(MemoryUsageLevel)),
            ("FilterLens_Property_", typeof(EventProperty)),
            ("FilterEditor_Comparison_", typeof(ComparisonOperatorSelect.ComparisonKind))
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

        Assert.Contains("Severity_Unknown", resxKeys);
    }

    [Fact]
    public void EveryProductionLiteralKeyReference_ExistsInNeutralResx()
    {
        // Extract every identifier-like string literal INSIDE a Localizer[...] indexer or a .GetString(...) call -
        // including keys selected by a conditional such as Localizer[count == 1 ? "..._One" : "..._Many", count] and
        // composite keys such as Localizer["...", arg] - so a key referenced only through a ternary cannot silently drift
        // out of the RESX. Format arguments are variables or punctuation separators (", ", " "), never identifier-like
        // literals, so scanning the whole call span never misreads an argument as a key. LocalizedCount.OneOrMany picks
        // its key by count at runtime, so its singular/plural pair never lands inside a Localizer[...] indexer; a second
        // pattern anchored on the trailing quoted pair recovers both keys (a parenthesis in the count arg cannot truncate it).
        var localizerCallPattern = new Regex(
            @"[Ll]ocalizer\[([^\]]*)\]|\.GetString\(([^)]*)\)",
            RegexOptions.Compiled);
        var keyLiteralPattern = new Regex(@"""([A-Za-z0-9_]+)""", RegexOptions.Compiled);
        var oneOrManyPattern = new Regex(
            @"OneOrMany\([^;{}]*?""([A-Za-z0-9_]+)""\s*,\s*""([A-Za-z0-9_]+)""\s*\)",
            RegexOptions.Compiled);

        var sources = LocalizationSourceScan.EnumerateProductionSource()
            .Select(File.ReadAllText)
            .ToList();

        var referenced = sources
            .SelectMany(source => localizerCallPattern.Matches(source))
            .SelectMany(call => keyLiteralPattern.Matches(call.Groups[1].Value + call.Groups[2].Value))
            .Select(match => match.Groups[1].Value)
            .Concat(sources
                .SelectMany(source => oneOrManyPattern.Matches(source))
                .SelectMany(call => new[] { call.Groups[1].Value, call.Groups[2].Value }))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        var authored = ResxKeys().ToHashSet(StringComparer.Ordinal);
        var missing = referenced.Where(key => !authored.Contains(key)).ToList();

        Assert.True(referenced.Count >= 150, $"Production literal localizer scan found only {referenced.Count} keys.");
        Assert.True(missing.Count == 0, $"Production literal localizer keys missing from RESX: {string.Join(", ", missing)}");
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
    public void Localizer_KnownKey_Resolves()
    {
        var key = "FindBar_NoResults";
        var result = BuildLocalizer()[key];

        Assert.False(result.ResourceNotFound);
        Assert.NotEqual(key, result.Value);
    }

    [Fact]
    public void Localizer_MissingKey_ReportsNotFoundAndEchoesKey()
    {
        var result = BuildLocalizer()["FindBar_ThisKeyDoesNotExist"];

        Assert.True(result.ResourceNotFound);
        Assert.Equal("FindBar_ThisKeyDoesNotExist", result.Value);
    }

    [Fact]
    public void NeutralCoverageStatusValues_MirrorLabels()
    {
        // The status cell localizes CoverageStatus while the TSV/copy path renders CoverageStatusText.Label. This pins
        // each neutral Coverage_Status_* value equal to that invariant label so the on-screen pill and the copied table
        // never disagree. (No severity counterpart: SeverityLevel has no invariant twin consumer.)
        var neutralValues = ResxValues();

        foreach (CoverageStatus status in Enum.GetValues<CoverageStatus>())
        {
            var key = $"Coverage_Status_{status}";

            Assert.True(neutralValues.TryGetValue(key, out var neutral), $"Missing neutral RESX value for {key}.");
            Assert.Equal(CoverageStatusText.Label(status), neutral);
        }
    }

    [Fact]
    public void NeutralFilterLensPropertyValues_MirrorToFullString()
    {
        // The UI FilterLensLabelFormatter localizes the FilterLensLabel.PropertyComparison property name; the invariant
        // baseline (FilterLensLabelText.Invariant) and the filter value picker both render property.ToFullString(). This
        // pins each neutral FilterLens_Property_* value equal to that canonical string (read straight from the neutral
        // RESX) so the chip, the picker, and the invariant announcement never disagree.
        var neutralValues = ResxValues();

        foreach (EventProperty property in Enum.GetValues<EventProperty>())
        {
            var key = $"FilterLens_Property_{property}";

            Assert.True(neutralValues.TryGetValue(key, out var neutral), $"Missing neutral RESX value for {key}.");
            Assert.Equal(property.ToFullString(), neutral);
        }
    }

    [Fact]
    public void NeutralHistogramCountsRemainUngrouped()
    {
        IStringLocalizer<SharedResource> localizer = BuildLocalizer();

        Assert.Equal(
            "Other (1200 sources)",
            HistogramGroupLabelFormatter.Format(localizer, new HistogramGroupLabel.CategoricalOther(HistogramDimension.Source, 1200)));
    }

    [Fact]
    public void NeutralHistogramDimensionValues_MirrorPreviousDisplayText()
    {
        var neutralValues = ResxValues();
        (HistogramDimension Dimension, string Text)[] expected =
        [
            (HistogramDimension.Severity, "Severity"),
            (HistogramDimension.Source, "Source"),
            (HistogramDimension.EventId, "Event ID"),
            (HistogramDimension.TaskCategory, "Task Category"),
            (HistogramDimension.Opcode, "Opcode"),
            (HistogramDimension.Log, "Log"),
            (HistogramDimension.LogonType, "Logon Type"),
            (HistogramDimension.TicketEncryptionType, "Ticket Encryption Type"),
            (HistogramDimension.ErrorCode, "Error Code"),
            (HistogramDimension.ProcessImage, "Process Image"),
            (HistogramDimension.ParentProcessImage, "Parent Process Image")
        ];

        foreach ((HistogramDimension dimension, string text) in expected)
        {
            Assert.Equal(text, neutralValues[$"Histogram_Dimension_{dimension}"]);
        }
    }

    [Fact]
    public void NeutralHistogramSpaceBearingValues_KeepExactWhitespace()
    {
        var neutralValues = ResxValues();

        Assert.Equal(", ", neutralValues["Histogram_Breakdown_Separator"]);
        Assert.Equal(" - {0} error/critical", neutralValues["Stats_Headline_ErrorCritical_One"]);
        Assert.Equal(" - {0} error/critical", neutralValues["Stats_Headline_ErrorCritical_Many"]);
        Assert.Equal(" - top {0} source = {1}%", neutralValues["Stats_Headline_TopSources_One"]);
        Assert.Equal(" - top {0} sources = {1}%", neutralValues["Stats_Headline_TopSources_Many"]);
    }

    [Fact]
    public void NeutralHistogramSummaryTemplates_KeepGeneralShortDateFormat()
    {
        var neutralValues = ResxValues();

        Assert.Equal("Timeline: {0} {1} from {2:g} to {3:g}.", neutralValues["Histogram_RegionAria"]);
        Assert.Equal("Timeline: {0} {1} from {2:g} to {3:g}, {4}.", neutralValues["Histogram_RegionAria_Breakdown"]);
        Assert.Equal("Showing {2:g} to {3:g}: {0} {1}.", neutralValues["Histogram_WindowAnnouncement"]);
        Assert.Equal("Showing {2:g} to {3:g}: {0} {1}, {4}.", neutralValues["Histogram_WindowAnnouncement_Breakdown"]);
    }

    [Fact]
    public void NeutralPropertyLabelValues_MirrorInvariant()
    {
        // The formatter emits the typed DetailsPropertyLabel; copy renders it via DetailsPropertyText.Invariant. This
        // pins each neutral Details_Property_* value equal to that invariant (read straight from the neutral RESX, so a
        // shipped translation under an ambient culture cannot red it), mirroring the ScenarioGroup drift guard.
        var neutralValues = ResxValues();

        foreach (DetailsPropertyLabel label in Enum.GetValues<DetailsPropertyLabel>())
        {
            var key = $"Details_Property_{label}";

            Assert.True(neutralValues.TryGetValue(key, out var neutral), $"Missing neutral RESX value for {key}.");
            Assert.Equal(DetailsPropertyText.Invariant(label), neutral);
        }
    }

    [Fact]
    public void NeutralResolutionStatusValues_MirrorTokens()
    {
        // The status display localizes EventResolutionStatus while copy/filter/storage use the invariant token. This
        // pins each neutral ResolutionStatus_* value equal to ResolutionStatusTokens.Format so display and token agree.
        var neutralValues = ResxValues();

        foreach (EventResolutionStatus status in Enum.GetValues<EventResolutionStatus>())
        {
            var key = $"ResolutionStatus_{status}";

            Assert.True(neutralValues.TryGetValue(key, out var neutral), $"Missing neutral RESX value for {key}.");
            Assert.Equal(ResolutionStatusTokens.Format(status), neutral);
        }
    }

    [Fact]
    public void NeutralStatsDimensionValues_MirrorPreviousDisplayText()
    {
        var neutralValues = ResxValues();
        (StatsDimension Dimension, string Text)[] expected =
        [
            (StatsDimension.Source, "Source"),
            (StatsDimension.EventId, "Event ID"),
            (StatsDimension.TaskCategory, "Task Category"),
            (StatsDimension.User, "User")
        ];

        foreach ((StatsDimension dimension, string text) in expected)
        {
            Assert.Equal(text, neutralValues[$"Stats_Dimension_{dimension}"]);
        }
    }

    [Fact]
    public void NeutralStatusBarSpaceBearingValues_KeepXmlSpaceAndExactWhitespace()
    {
        XNamespace xmlNamespace = XNamespace.Xml;
        var data = XDocument.Load(LocalizationSourceScan.ResxPath)
            .Root!
            .Elements("data")
            .Where(element => ((string?)element.Attribute("name"))?.StartsWith("StatusBar_", StringComparison.Ordinal) == true)
            .ToDictionary(element => (string)element.Attribute("name")!, StringComparer.Ordinal);

        string[] spaceBearingKeys =
        [
            "StatusBar_Counts_TotalSelected",
            "StatusBar_Counts_ShownOfTotalSelected",
            "StatusBar_Memory_Value_Elevated",
            "StatusBar_Memory_Value_High"
        ];

        foreach (string key in spaceBearingKeys)
        {
            Assert.Equal("preserve", data[key].Attribute(xmlNamespace + "space")?.Value);
        }
    }

    [Fact]
    public void NeutralStatusBarValues_KeepByteIdenticalEnglish()
    {
        var neutralValues = ResxValues();
        (string Key, string Value)[] expected =
        [
            ("StatusBar_Source_None", "No log open"),
            ("StatusBar_Source_AllLogs", "All logs ({0})"),
            ("StatusBar_Source_Combined", "Combined"),
            ("StatusBar_Source_CombinedCount_One", "Combined ({0} logs)"),
            ("StatusBar_Source_CombinedCount_Many", "Combined ({0} logs)"),
            ("StatusBar_Counts_Total_One", "{0} events"),
            ("StatusBar_Counts_Total_Many", "{0} events"),
            ("StatusBar_Counts_TotalSelected", "{0} events · {1} selected"),
            ("StatusBar_Counts_ShownOfTotal", "{0} of {1} shown"),
            ("StatusBar_Counts_ShownOfTotalSelected", "{0} of {1} shown · {2} selected"),
            ("StatusBar_Coverage_Chip", "{0} unresolved"),
            ("StatusBar_Coverage_AriaLabel", "Resolution and coverage: {0} unresolved. Open for details."),
            ("StatusBar_Coverage_Tooltip", "{0} unresolved of {1} events loaded in this tab/group. Filters are not applied - open Coverage for the current view's breakdown."),
            ("StatusBar_Loading_Pending", "Loading..."),
            ("StatusBar_Loading_PendingPercent", "Loading... ({0}%)"),
            ("StatusBar_Loading_Count", "Loading: {0}"),
            ("StatusBar_Loading_CountPercent", "Loading: {0} ({1}%)"),
            ("StatusBar_Loading_ManyLogs", "Loading {0} logs..."),
            ("StatusBar_Loading_Failed", "Failed: {0}"),
            ("StatusBar_Memory_Value_Normal", "Memory: {0}"),
            ("StatusBar_Memory_Value_Elevated", "Memory: {0} · Elevated"),
            ("StatusBar_Memory_Value_High", "Memory: {0} · High"),
            ("StatusBar_Memory_Announce_Normal", "Memory usage normal"),
            ("StatusBar_Memory_Announce_Elevated", "Memory usage elevated"),
            ("StatusBar_Memory_Announce_High", "Memory usage high"),
            ("StatusBar_Memory_Tooltip_Normal", "Managed heap (app data): {0} - drops as logs close. Process working set: {1} - the OS may release this later."),
            ("StatusBar_Memory_Tooltip_Elevated", "Managed heap (app data): {0} - drops as logs close. Process working set: {1} - the OS may release this later. Level: elevated."),
            ("StatusBar_Memory_Tooltip_High", "Managed heap (app data): {0} - drops as logs close. Process working set: {1} - the OS may release this later. Level: high."),
            ("StatusBar_Activity_Fault", "These events could not be prepared"),
            ("StatusBar_Activity_BufferFull", "Buffer full"),
            ("StatusBar_Activity_Loading", "Loading"),
            ("StatusBar_Activity_LoadingEvents", "Loading events"),
            ("StatusBar_Activity_Reordering", "Reordering events"),
            ("StatusBar_Activity_ContinuouslyUpdating", "Continuously updating"),
            ("StatusBar_Filter_Chip", "Filtered"),
            ("StatusBar_Filter_Active", "Filter active"),
            ("StatusBar_Filter_Lens_One", "1 lens"),
            ("StatusBar_Filter_Lens_Many", "{0} lenses"),
            ("StatusBar_Filter_ActiveLens_One", "Filter + 1 lens"),
            ("StatusBar_Filter_ActiveLens_Many", "Filter + {0} lenses"),
            ("StatusBar_Stats_Show", "Show statistics for these events"),
            ("StatusBar_Stats_Hide", "Hide statistics for these events"),
            ("StatusBar_NewEvents_Label", "New Events: {0}"),
            ("StatusBar_NewEvents_None", "No new events to load"),
            ("StatusBar_NewEvents_Load", "Load new events into the view")
        ];

        foreach ((string key, string value) in expected)
        {
            Assert.True(neutralValues.TryGetValue(key, out string? actual), $"Missing neutral RESX value for {key}.");
            Assert.Equal(value, actual);
        }

        Assert.Equal(
            expected.Select(entry => entry.Key).OrderBy(key => key, StringComparer.Ordinal),
            neutralValues.Keys.Where(key => key.StartsWith("StatusBar_", StringComparison.Ordinal)).OrderBy(key => key, StringComparer.Ordinal));
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
    public void StatusBarMemorySizes_UseCurrentCultureDecimalSeparator()
    {
        CultureInfo priorCulture = CultureInfo.CurrentCulture;
        CultureInfo priorUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");

            string value = StatusBarTextComposer.MemoryValue(BuildLocalizer(), 1536, MemoryUsageLevel.Normal);

            Assert.Contains("1,5", value, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
            CultureInfo.CurrentUICulture = priorUiCulture;
        }
    }

    [Fact]
    public void StatusBarNumberFormatting_MixesGroupedLoadingAndRawNewEvents()
    {
        IStringLocalizer<SharedResource> localizer = BuildLocalizer();
        var loading = StatusBarTextComposer.Loading(
            localizer,
            ImmutableDictionary<StatusActivityId, LoadingProgress>.Empty.Add(
                StatusActivityId.Create(),
                new LoadingProgress(1500, 0)));

        Assert.NotNull(loading);
        Assert.Contains(1500.ToString("N0", CultureInfo.CurrentCulture), loading.Value.Text, StringComparison.Ordinal);

        using var context = new StatusBarRenderContext(newEventCount: 1000);

        var cut = context.Render<UI.StatusBar.StatusBar>();

        Assert.Contains("1000", cut.Find("button.status-bar-newevents").TextContent, StringComparison.Ordinal);
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

    private static IReadOnlyDictionary<string, string> ResxValues() =>
        XDocument.Load(LocalizationSourceScan.ResxPath)
            .Root!.Elements("data")
            .Where(data => data.Attribute("name") is not null)
            .ToDictionary(
                data => (string)data.Attribute("name")!,
                data => data.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);

    private sealed class StatusBarRenderContext : BunitContext
    {
        public StatusBarRenderContext(int newEventCount)
        {
            JSInterop.Mode = JSRuntimeMode.Loose;

            var eventLogCommands = Substitute.For<IEventLogCommands>();
            var filterApplied = Substitute.For<IFilterAppliedSource>();
            var lensSource = Substitute.For<IFilterLensSource>();
            var modalCoordinator = Substitute.For<IModalCoordinator>();
            var statsCommands = Substitute.For<IStatsCommands>();
            var statsVisibility = Substitute.For<IStatsVisibilitySource>();
            var statusBarSource = Substitute.For<IStatusBarSource>();
            var viewSource = Substitute.For<IOrderedViewSource>();

            var eventLogId = EventLogId.Create();
            var view = Substitute.For<IEventColumnView>();
            view.Count.Returns(0);

            viewSource.Current.Returns(_ => new OrderedViewPresentation(view, eventLogId, default, PresentationState.Current, Revision: 1));
            filterApplied.IsFilteringEnabled.Returns(false);
            lensSource.Lenses.Returns(ImmutableList<FilterLensSummary>.Empty);
            statusBarSource.Current.Returns(new StatusBarPresentation
            {
                Tabs = ImmutableList.Create(new LogView(eventLogId) { LogName = "Application", LogPathType = LogPathType.Channel }),
                ActiveTabId = eventLogId,
                RawEventCountsByLog = ImmutableDictionary<EventLogId, ProviderResolutionCounts>.Empty.Add(eventLogId, default),
                NewEventBufferCount = newEventCount
            });

            Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            Services.AddEventLogLocalization();
            Services.AddSingleton(eventLogCommands);
            Services.AddSingleton(filterApplied);
            Services.AddSingleton(lensSource);
            Services.AddSingleton(modalCoordinator);
            Services.AddSingleton(statsCommands);
            Services.AddSingleton(statsVisibility);
            Services.AddSingleton(statusBarSource);
            Services.AddSingleton(viewSource);
            Services.AddSingleton(provider => new DisplayIndicatorGate(provider.GetRequiredService<IOrderedViewSource>()));
        }
    }
}
