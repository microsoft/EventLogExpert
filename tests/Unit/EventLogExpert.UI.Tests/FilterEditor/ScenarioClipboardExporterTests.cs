// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Scenarios.Catalog;
using EventLogExpert.UI.FilterEditor;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.FilterEditor;

public sealed class ScenarioClipboardExporterTests
{
    private readonly IAlertDialogService _alertDialog = Substitute.For<IAlertDialogService>();
    private readonly IAnnouncementService _announcements = Substitute.For<IAnnouncementService>();
    private readonly IClipboardService _clipboard = Substitute.For<IClipboardService>();

    [Fact]
    public async Task AnnounceAsync_WhenChannelsEmpty_AppendsChannelsGuidance()
    {
        await CreateExporter().AnnounceAsync(
            "Scenario JSON copied to the clipboard.",
            [ScenarioExporter.NoLiveChannelsWarning]);

        _announcements.Received(1).Announce(
            "Scenario JSON copied to the clipboard. Fill in channels[] before adding to the catalog.");
    }

    [Fact]
    public async Task AnnounceAsync_WhenSubstantiveWarnings_ShowsAlertNotAnnouncement()
    {
        await CreateExporter().AnnounceAsync("Saved.", ["single-row color guardrail"]);

        await _alertDialog.Received(1).ShowAlert(
            "Scenario JSON exported with warnings",
            Arg.Is<string>(message => message.Contains("single-row color guardrail")),
            "OK");
        _announcements.DidNotReceive().Announce(Arg.Any<string>());
    }

    [Fact]
    public async Task CopyAsync_WhenExportable_CopiesJsonAndAnnounces()
    {
        var export = new ScenarioExportResult("{ }", ImmutableList<string>.Empty, EmittedRowCount: 1);

        await CreateExporter().CopyAsync(export, "Copied.", "these filters");

        await _clipboard.Received(1).CopyTextAsync("{ }");
        _announcements.Received(1).Announce("Copied.");
    }

    [Fact]
    public async Task CopyAsync_WhenNothingEmitted_DoesNotCopy()
    {
        var export = new ScenarioExportResult(string.Empty, ImmutableList<string>.Empty, EmittedRowCount: 0);

        await CreateExporter().CopyAsync(export, "Copied.", "this filter");

        await _clipboard.DidNotReceive().CopyTextAsync(Arg.Any<string>());
        _announcements.Received(1)
            .Announce("Could not export this filter: only Basic filters can be exported as scenarios.");
    }

    [Fact]
    public void NotExportable_WhenNothingEmitted_AnnouncesAndReturnsTrue()
    {
        var export = new ScenarioExportResult(string.Empty, ImmutableList<string>.Empty, EmittedRowCount: 0);

        Assert.True(CreateExporter().NotExportable(export, "these filters"));
        _announcements.Received(1)
            .Announce("Could not export these filters: only Basic filters can be exported as scenarios.");
    }

    [Fact]
    public void NotExportable_WhenRowsEmitted_ReturnsFalseWithoutAnnouncing()
    {
        var export = new ScenarioExportResult("{}", ImmutableList<string>.Empty, EmittedRowCount: 2);

        Assert.False(CreateExporter().NotExportable(export, "these filters"));
        _announcements.DidNotReceive().Announce(Arg.Any<string>());
    }

    [Fact]
    public void NotExportable_WhenRowsSkipped_SurfacesSkippedCount()
    {
        var export = new ScenarioExportResult(
            string.Empty,
            ["3 row(s) skipped: not expressible as a Basic filter."],
            EmittedRowCount: 0);

        Assert.True(CreateExporter().NotExportable(export, "these filters"));
        _announcements.Received(1)
            .Announce("Could not export these filters: 3 row(s) skipped: not expressible as a Basic filter.");
    }

    private ScenarioClipboardExporter CreateExporter() => new(_announcements, _alertDialog, _clipboard);
}
