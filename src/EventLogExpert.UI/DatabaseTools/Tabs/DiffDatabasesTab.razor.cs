// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.DiffDatabase;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.DatabaseTools.Tabs;

public sealed partial class DiffDatabasesTab : DatabaseToolsTabBase<DiffDatabaseRequest>
{
    private static readonly IReadOnlyList<string> s_dbExtensions = [".db"];
    private static readonly IReadOnlyList<string> s_sourceExtensions = [".db", ".evtx"];

    private string _firstPath = string.Empty;
    private string _newDbPath = string.Empty;
    private string _secondPath = string.Empty;

    protected override bool CanRun =>
        !string.IsNullOrWhiteSpace(_firstPath) &&
        !string.IsNullOrWhiteSpace(_secondPath) &&
        !string.IsNullOrWhiteSpace(_newDbPath);

    protected override DiffDatabaseRequest BuildRequest() =>
        new(_firstPath.Trim(), _secondPath.Trim(), _newDbPath.Trim());

    protected override Task<DatabaseToolsResult> DispatchAsync(
        DiffDatabaseRequest request,
        IProgress<DatabaseToolsLogEntry> logSink,
        CancellationToken cancellationToken) =>
        DatabaseToolsService.DiffAsync(request, logSink, progress: null, cancellationToken, VerboseLogging);

    private void OnFirstPathInput(ChangeEventArgs e) => _firstPath = e.Value?.ToString() ?? string.Empty;

    private void OnNewDbPathInput(ChangeEventArgs e) => _newDbPath = e.Value?.ToString() ?? string.Empty;

    private void OnSecondPathInput(ChangeEventArgs e) => _secondPath = e.Value?.ToString() ?? string.Empty;

    private async Task PickFirstAsync()
    {
        var path = await PickFileAsync("Pick first source (.db or .evtx)", s_sourceExtensions);
        if (!string.IsNullOrEmpty(path)) { _firstPath = path; }
    }

    private async Task PickNewDbAsync()
    {
        var path = await PickSaveFileAsync("Pick output .db (or type a new name)", s_dbExtensions, "diff.db");
        if (!string.IsNullOrEmpty(path)) { _newDbPath = path; }
    }

    private async Task PickSecondAsync()
    {
        var path = await PickFileAsync("Pick second source (.db or .evtx)", s_sourceExtensions);
        if (!string.IsNullOrEmpty(path)) { _secondPath = path; }
    }
}
