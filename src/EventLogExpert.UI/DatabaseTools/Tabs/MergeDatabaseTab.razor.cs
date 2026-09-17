// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.MergeDatabase;
using EventLogExpert.Logging.Abstractions;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.DatabaseTools.Tabs;

public sealed partial class MergeDatabaseTab : DatabaseToolsTabBase<MergeDatabaseRequest>
{
    private static readonly IReadOnlyList<string> s_dbExtensions = [".db"];
    private static readonly IReadOnlyList<string> s_sourceExtensions = [".db", ".evtx"];

    private MergeOverwriteMode _overwriteMode = MergeOverwriteMode.Keep;
    private string _sourcePath = string.Empty;
    private string _targetPath = string.Empty;

    protected override bool CanRun =>
        !string.IsNullOrWhiteSpace(_sourcePath) && !string.IsNullOrWhiteSpace(_targetPath);

    protected override string LogCategory => LogCategories.DatabaseToolsMerge;

    protected override string? ProducedDatabasePathCandidate => _targetPath.Trim();

    protected override MergeDatabaseRequest BuildRequest() =>
        new(_sourcePath.Trim(), _targetPath.Trim(), _overwriteMode == MergeOverwriteMode.Overwrite);

    protected override Task<DatabaseToolsResult> DispatchAsync(
        MergeDatabaseRequest request,
        IProgress<LogRecord> logProgress,
        CancellationToken cancellationToken) =>
        DatabaseToolsService.MergeAsync(request, logProgress, progress: null, cancellationToken, VerboseLogging);

    private string FormatOverwriteMode(MergeOverwriteMode mode) => MergeOverwriteModeLocalizer.Label(Localizer, mode);

    private void OnSourcePathInput(ChangeEventArgs e) => _sourcePath = e.Value?.ToString() ?? string.Empty;

    private void OnTargetPathInput(ChangeEventArgs e) => _targetPath = e.Value?.ToString() ?? string.Empty;

    private async Task PickSourceAsync()
    {
        var path = await PickFileAsync(Localizer["DatabaseTools_Picker_SourceDbOrEvtx"], s_sourceExtensions);

        if (!string.IsNullOrEmpty(path)) { _sourcePath = path; }
    }

    private async Task PickTargetAsync()
    {
        var path = await PickFileAsync(Localizer["Db_Merge_Picker_TargetDb"], s_dbExtensions);

        if (!string.IsNullOrEmpty(path)) { _targetPath = path; }
    }
}
