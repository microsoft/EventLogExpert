// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Scenarios.Catalog;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.FilterEditor;

internal sealed class ScenarioClipboardExporter
{
    private readonly IAlertDialogService _alertDialogService;
    private readonly IAnnouncementService _announcementService;
    private readonly IClipboardService _clipboardService;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public ScenarioClipboardExporter(
        IAnnouncementService announcementService,
        IAlertDialogService alertDialogService,
        IClipboardService clipboardService,
        IStringLocalizer<SharedResource> localizer)
    {
        ArgumentNullException.ThrowIfNull(announcementService);
        ArgumentNullException.ThrowIfNull(alertDialogService);
        ArgumentNullException.ThrowIfNull(clipboardService);
        ArgumentNullException.ThrowIfNull(localizer);

        _announcementService = announcementService;
        _alertDialogService = alertDialogService;
        _clipboardService = clipboardService;
        _localizer = localizer;
    }

    public async Task AnnounceAsync(string success, IReadOnlyList<string> warnings)
    {
        var message = warnings.Contains(ScenarioExporter.NoLiveChannelsWarning) ?
            _localizer["ScenarioExport_WithChannelsGuidance", success, _localizer["ScenarioExport_ChannelsGuidance"]] :
            success;

        var substantive = SubstantiveWarnings(warnings);

        if (substantive.Count == 0)
        {
            _announcementService.Announce(message);

            return;
        }

        await _alertDialogService.ShowAlert(
            _localizer["ScenarioExport_WarningsTitle"],
            _localizer["ScenarioExport_WarningsBody", message, Environment.NewLine, string.Join(Environment.NewLine, substantive)],
            _localizer["Modal_Accept"]);
    }

    public async Task CopyAsync(ScenarioExportResult export, string success, ScenarioExportSubject subject)
    {
        if (NotExportable(export, subject)) { return; }

        await _clipboardService.CopyTextAsync(export.Json);
        await AnnounceAsync(success, export.Warnings);
    }

    public bool NotExportable(ScenarioExportResult export, ScenarioExportSubject subject)
    {
        if (export.EmittedRowCount > 0) { return false; }

        var detail = SubstantiveWarnings(export.Warnings);

        _announcementService.Announce(NotExportableMessage(subject, detail));

        return true;
    }

    private static IReadOnlyList<string> SubstantiveWarnings(IReadOnlyList<string> warnings) =>
        [.. warnings.Where(warning => warning != ScenarioExporter.NoLiveChannelsWarning)];

    private string NotExportableMessage(ScenarioExportSubject subject, IReadOnlyList<string> detail) =>
        (subject, detail.Count > 0) switch
        {
            (ScenarioExportSubject.SingleFilter, true) =>
                _localizer["ScenarioExport_NotExportable_SingleFilter_WithDetail", string.Join(" ", detail)],
            (ScenarioExportSubject.SingleFilter, false) =>
                _localizer["ScenarioExport_NotExportable_SingleFilter_BasicOnly"],
            (ScenarioExportSubject.CurrentFilters, true) =>
                _localizer["ScenarioExport_NotExportable_CurrentFilters_WithDetail", string.Join(" ", detail)],
            (ScenarioExportSubject.CurrentFilters, false) =>
                _localizer["ScenarioExport_NotExportable_CurrentFilters_BasicOnly"],
            (ScenarioExportSubject.FilterSet, true) =>
                _localizer["ScenarioExport_NotExportable_FilterSet_WithDetail", string.Join(" ", detail)],
            (ScenarioExportSubject.FilterSet, false) =>
                _localizer["ScenarioExport_NotExportable_FilterSet_BasicOnly"],
            _ => throw new ArgumentOutOfRangeException(nameof(subject), subject, null),
        };
}
