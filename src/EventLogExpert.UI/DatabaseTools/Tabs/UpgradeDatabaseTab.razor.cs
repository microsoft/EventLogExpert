// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.UpgradeDatabase;
using EventLogExpert.Logging.Abstractions;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.DatabaseTools.Tabs;

public sealed partial class UpgradeDatabaseTab : DatabaseToolsTabBase<UpgradeDatabaseRequest>
{
    private static readonly IReadOnlyList<string> s_dbExtensions = [".db"];

    private string _dbPath = string.Empty;

    protected override bool CanRun => !string.IsNullOrWhiteSpace(_dbPath);

    protected override string LogCategory => LogCategories.DatabaseToolsUpgrade;

    protected override UpgradeDatabaseRequest BuildRequest() => new(_dbPath.Trim());

    protected override Task<DatabaseToolsResult> DispatchAsync(
        UpgradeDatabaseRequest request,
        IProgress<LogRecord> logSink,
        CancellationToken cancellationToken) =>
        DatabaseToolsService.UpgradeAsync(request, logSink, progress: null, cancellationToken, VerboseLogging);

    private void OnDbPathInput(ChangeEventArgs e) => _dbPath = e.Value?.ToString() ?? string.Empty;

    private async Task PickDbAsync()
    {
        var path = await PickFileAsync("Pick .db to upgrade", s_dbExtensions);
        if (!string.IsNullOrEmpty(path)) { _dbPath = path; }
    }
}
