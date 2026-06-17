// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Scenarios.Catalog;

namespace EventLogExpert.UI.FilterEditor;

internal sealed class ScenarioClipboardExporter
{
    private const string ChannelsGuidance = "Fill in channels[] before adding to the catalog.";

    private readonly IAlertDialogService _alertDialogService;
    private readonly IAnnouncementService _announcementService;
    private readonly IClipboardService _clipboardService;

    public ScenarioClipboardExporter(
        IAnnouncementService announcementService,
        IAlertDialogService alertDialogService,
        IClipboardService clipboardService)
    {
        ArgumentNullException.ThrowIfNull(announcementService);
        ArgumentNullException.ThrowIfNull(alertDialogService);
        ArgumentNullException.ThrowIfNull(clipboardService);

        _announcementService = announcementService;
        _alertDialogService = alertDialogService;
        _clipboardService = clipboardService;
    }

    public async Task AnnounceAsync(string success, IReadOnlyList<string> warnings)
    {
        var message = warnings.Contains(ScenarioExporter.NoLiveChannelsWarning)
            ? $"{success} {ChannelsGuidance}"
            : success;

        var substantive = SubstantiveWarnings(warnings);

        if (substantive.Count == 0)
        {
            _announcementService.Announce(message);

            return;
        }

        await _alertDialogService.ShowAlert(
            "Scenario JSON exported with warnings",
            message + Environment.NewLine + string.Join(Environment.NewLine, substantive),
            "OK");
    }

    public async Task CopyAsync(ScenarioExportResult export, string success, string emptyNoun)
    {
        if (NotExportable(export, emptyNoun)) { return; }

        await _clipboardService.CopyTextAsync(export.Json);
        await AnnounceAsync(success, export.Warnings);
    }

    public bool NotExportable(ScenarioExportResult export, string emptyNoun)
    {
        if (export.EmittedRowCount > 0) { return false; }

        var detail = SubstantiveWarnings(export.Warnings);

        _announcementService.Announce(detail.Count > 0
            ? $"Could not export {emptyNoun}: {string.Join(" ", detail)}"
            : $"Could not export {emptyNoun}: only Basic filters can be exported as scenarios.");

        return true;
    }

    private static IReadOnlyList<string> SubstantiveWarnings(IReadOnlyList<string> warnings) =>
        [.. warnings.Where(warning => warning != ScenarioExporter.NoLiveChannelsWarning)];
}
