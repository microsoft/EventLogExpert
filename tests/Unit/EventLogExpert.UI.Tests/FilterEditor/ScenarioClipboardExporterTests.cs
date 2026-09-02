// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Scenarios.Catalog;
using EventLogExpert.UI.FilterEditor;
using EventLogExpert.UI.Tests.TestUtils;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.FilterEditor;

public sealed class ScenarioClipboardExporterTests
{
    private readonly IAlertDialogService _alertDialog = Substitute.For<IAlertDialogService>();
    private readonly IAnnouncementService _announcements = Substitute.For<IAnnouncementService>();
    private readonly IClipboardService _clipboard = Substitute.For<IClipboardService>();

    [Fact]
    public async Task AnnounceAsync_WhenChannelsEmptyAndSubstantiveWarnings_ShowsLocalizedBodyWithGuidanceAndLiteralWarningData()
    {
        const string Warning = "single-row color guardrail";

        await CreateExporter().AnnounceAsync("Saved.", [ScenarioExporter.NoLiveChannelsWarning, Warning]);

        await _alertDialog.Received(1).ShowAlert(
            "[[ScenarioExport_WarningsTitle]]",
            $"[[ScenarioExport_WarningsBody([[ScenarioExport_WithChannelsGuidance(Saved.|[[ScenarioExport_ChannelsGuidance]])]]|{Environment.NewLine}|{Warning})]]",
            "[[Modal_Accept]]");
        _announcements.DidNotReceive().Announce(Arg.Any<string>());
    }

    [Fact]
    public async Task AnnounceAsync_WhenChannelsEmpty_RoutesGuidanceThroughLocalizer()
    {
        await CreateExporter().AnnounceAsync(
            "Scenario JSON copied to the clipboard.",
            [ScenarioExporter.NoLiveChannelsWarning]);

        _announcements.Received(1).Announce(
            "[[ScenarioExport_WithChannelsGuidance(Scenario JSON copied to the clipboard.|[[ScenarioExport_ChannelsGuidance]])]]");
    }

    [Fact]
    public async Task AnnounceAsync_WhenSubstantiveWarnings_ShowsLocalizedAlertWithLiteralWarningData()
    {
        const string Warning = "single-row color guardrail";

        await CreateExporter().AnnounceAsync("Saved.", [Warning]);

        await _alertDialog.Received(1).ShowAlert(
            "[[ScenarioExport_WarningsTitle]]",
            $"[[ScenarioExport_WarningsBody(Saved.|{Environment.NewLine}|{Warning})]]",
            "[[Modal_Accept]]");
        _announcements.DidNotReceive().Announce(Arg.Any<string>());
    }

    [Fact]
    public async Task CopyAsync_WhenExportable_CopiesJsonAndAnnounces()
    {
        var export = new ScenarioExportResult("{ }", ImmutableList<string>.Empty, EmittedRowCount: 1);

        await CreateExporter().CopyAsync(export, "Copied.", ScenarioExportSubject.CurrentFilters);

        await _clipboard.Received(1).CopyTextAsync("{ }");
        _announcements.Received(1).Announce("Copied.");
    }

    [Fact]
    public async Task CopyAsync_WhenNothingEmitted_DoesNotCopy()
    {
        var export = new ScenarioExportResult(string.Empty, ImmutableList<string>.Empty, EmittedRowCount: 0);

        await CreateExporter().CopyAsync(export, "Copied.", ScenarioExportSubject.SingleFilter);

        await _clipboard.DidNotReceive().CopyTextAsync(Arg.Any<string>());
        _announcements.Received(1)
            .Announce("[[ScenarioExport_NotExportable_SingleFilter_BasicOnly]]");
    }

    [Theory]
    [InlineData("SingleFilter", "ScenarioExport_NotExportable_SingleFilter_BasicOnly")]
    [InlineData("CurrentFilters", "ScenarioExport_NotExportable_CurrentFilters_BasicOnly")]
    [InlineData("FilterSet", "ScenarioExport_NotExportable_FilterSet_BasicOnly")]
    public void NotExportable_WhenNothingEmitted_RoutesSubjectBasicOnlyKey(
        string subjectName,
        string expectedKey)
    {
        var export = new ScenarioExportResult(string.Empty, ImmutableList<string>.Empty, EmittedRowCount: 0);

        Assert.True(CreateExporter().NotExportable(export, Enum.Parse<ScenarioExportSubject>(subjectName)));
        _announcements.Received(1).Announce($"[[{expectedKey}]]");
    }

    [Fact]
    public void NotExportable_WhenRowsEmitted_ReturnsFalseWithoutAnnouncing()
    {
        var export = new ScenarioExportResult("{}", ImmutableList<string>.Empty, EmittedRowCount: 2);

        Assert.False(CreateExporter().NotExportable(export, ScenarioExportSubject.CurrentFilters));
        _announcements.DidNotReceive().Announce(Arg.Any<string>());
    }

    [Theory]
    [InlineData("SingleFilter", "ScenarioExport_NotExportable_SingleFilter_WithDetail")]
    [InlineData("CurrentFilters", "ScenarioExport_NotExportable_CurrentFilters_WithDetail")]
    [InlineData("FilterSet", "ScenarioExport_NotExportable_FilterSet_WithDetail")]
    public void NotExportable_WhenRowsSkipped_RoutesSubjectDetailKeyWithLiteralWarningData(
        string subjectName,
        string expectedKey)
    {
        const string Warning = "3 row(s) skipped: not expressible as a Basic filter.";
        var export = new ScenarioExportResult(string.Empty, [Warning], EmittedRowCount: 0);

        Assert.True(CreateExporter().NotExportable(export, Enum.Parse<ScenarioExportSubject>(subjectName)));
        _announcements.Received(1).Announce($"[[{expectedKey}({Warning})]]");
    }

    private ScenarioClipboardExporter CreateExporter() => new(_announcements, _alertDialog, _clipboard, new MarkerLocalizer());
}
