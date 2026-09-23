// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.DiffDatabase;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.DatabaseTools;
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

    protected override string LogCategory => LogCategories.DatabaseToolsDiff;

    protected override string? RunDisabledReason =>
        string.IsNullOrWhiteSpace(_firstPath) ? Localizer["Db_Diff_RunDisabled_NoFirst"].Value :
        string.IsNullOrWhiteSpace(_secondPath) ? Localizer["Db_Diff_RunDisabled_NoSecond"].Value :
        string.IsNullOrWhiteSpace(_newDbPath) ? Localizer["Db_Diff_RunDisabled_NoNewDb"].Value : null;

    protected override DiffDatabaseRequest BuildRequest() =>
        new(_firstPath.Trim(), _secondPath.Trim(), _newDbPath.Trim());

    protected override Task<DatabaseToolsResult> DispatchAsync(
        DiffDatabaseRequest request,
        IProgress<LogRecord> logProgress,
        CancellationToken cancellationToken) =>
        DatabaseToolsService.DiffAsync(request, logProgress, progress: null, cancellationToken, VerboseLogging);

    private void OnFirstPathInput(ChangeEventArgs e) => _firstPath = e.Value?.ToString() ?? string.Empty;

    private void OnNewDbPathInput(ChangeEventArgs e) => _newDbPath = e.Value?.ToString() ?? string.Empty;

    private void OnSecondPathInput(ChangeEventArgs e) => _secondPath = e.Value?.ToString() ?? string.Empty;

    private async Task PickFirstAsync()
    {
        var path = await PickFileAsync(Localizer["Db_Diff_Picker_FirstSource"], s_sourceExtensions, DatabaseToolsPickRole.Source);

        if (!string.IsNullOrEmpty(path)) { _firstPath = path; }
    }

    private async Task PickNewDbAsync()
    {
        var path = await PickSaveFileAsync(Localizer["DatabaseTools_Picker_OutputDbNewName"],
            s_dbExtensions,
            DatabaseToolsPickRole.Output,
            "diff.db");

        if (!string.IsNullOrEmpty(path)) { _newDbPath = path; }
    }

    private async Task PickSecondAsync()
    {
        var path = await PickFileAsync(Localizer["Db_Diff_Picker_SecondSource"], s_sourceExtensions, DatabaseToolsPickRole.Source);

        if (!string.IsNullOrEmpty(path)) { _secondPath = path; }
    }
}
