// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.MergeDatabase;
using EventLogExpert.Logging.Abstractions;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.DatabaseTools.Tabs;

public sealed partial class MergeDatabaseTab : DatabaseToolsTabBase<MergeDatabaseRequest>
{
    private const string KeepModeLabel = "Keep - skip providers already in target";
    private const string OverwriteModeLabel = "Overwrite - replace providers already in target";

    private static readonly IReadOnlyList<string> s_dbExtensions = [".db"];
    private static readonly IReadOnlyList<string> s_sourceExtensions = [".db", ".evtx"];

    private bool _overwrite;
    private string _sourcePath = string.Empty;
    private string _targetPath = string.Empty;

    protected override bool CanRun =>
        !string.IsNullOrWhiteSpace(_sourcePath) && !string.IsNullOrWhiteSpace(_targetPath);

    protected override string? ProducedDatabasePathCandidate => _targetPath.Trim();

    protected override MergeDatabaseRequest BuildRequest() =>
        new(_sourcePath.Trim(), _targetPath.Trim(), _overwrite);

    protected override Task<DatabaseToolsResult> DispatchAsync(
        MergeDatabaseRequest request,
        IProgress<LogRecord> logSink,
        CancellationToken cancellationToken) =>
        DatabaseToolsService.MergeAsync(request, logSink, progress: null, cancellationToken, VerboseLogging);

    /// <summary>Full label for the selected existing-providers mode, shown in the collapsed select (matches the items).</summary>
    private static string FormatOverwriteMode(bool overwrite) => overwrite ? OverwriteModeLabel : KeepModeLabel;

    private void OnSourcePathInput(ChangeEventArgs e) => _sourcePath = e.Value?.ToString() ?? string.Empty;

    private void OnTargetPathInput(ChangeEventArgs e) => _targetPath = e.Value?.ToString() ?? string.Empty;

    private async Task PickSourceAsync()
    {
        var path = await PickFileAsync("Pick source (.db or .evtx)", s_sourceExtensions);
        if (!string.IsNullOrEmpty(path)) { _sourcePath = path; }
    }

    private async Task PickTargetAsync()
    {
        var path = await PickFileAsync("Pick target .db", s_dbExtensions);
        if (!string.IsNullOrEmpty(path)) { _targetPath = path; }
    }
}
