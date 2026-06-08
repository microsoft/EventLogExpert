// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Versioning;
using EventLogExpert.Runtime.DatabaseTools.Elevation;
using Microsoft.AspNetCore.Components;
using System.Text.RegularExpressions;

namespace EventLogExpert.UI.DatabaseTools.Tabs;

public sealed partial class CreateDatabaseTab : DatabaseToolsTabBase<CreateDatabaseRequest>
{
    private static readonly IReadOnlyList<string> s_dbExtensions = [".db"];
    private static readonly IReadOnlyList<string> s_sourceExtensions = [".db", ".evtx"];

    private Regex? _compiledFilter;
    private string? _filterError;
    private string _filterText = string.Empty;
    private string _skipPath = string.Empty;
    private string _sourcePath = string.Empty;
    private string _targetPath = string.Empty;

    protected override bool CanRun => _filterError is null && !string.IsNullOrWhiteSpace(_targetPath);

    [Inject] private ICurrentVersionProvider CurrentVersionProvider { get; init; } = null!;

    [Inject] private IElevatedDatabaseToolsRunner ElevatedRunner { get; init; } = null!;

    private string FilterCssClass => _filterError is null ? "input filter" : "input filter invalid";

    /// <summary>Show the elevation warning only when source is empty (= local providers) AND not running elevated.</summary>
    private bool ShowElevationWarning =>
        string.IsNullOrWhiteSpace(_sourcePath) && !CurrentVersionProvider.IsAdmin;

    protected override CreateDatabaseRequest BuildRequest() =>
        new(
            _targetPath.Trim(),
            string.IsNullOrWhiteSpace(_sourcePath) ? null : _sourcePath.Trim(),
            _compiledFilter,
            string.IsNullOrWhiteSpace(_skipPath) ? null : _skipPath.Trim());

    protected override Task<DatabaseToolsResult> DispatchAsync(
        CreateDatabaseRequest request,
        IProgress<LogRecord> logSink,
        CancellationToken cancellationToken) =>
        DatabaseToolsService.CreateAsync(request, logSink, progress: null, cancellationToken, VerboseLogging);

    private void OnFilterInput(ChangeEventArgs e)
    {
        var pattern = e.Value?.ToString() ?? string.Empty;
        _filterText = pattern;

        _filterError = FilterRegexFactory.TryCreate(pattern, out _compiledFilter, out var error) ? null : $"Invalid regex: {error}";
    }

    private void OnSkipPathInput(ChangeEventArgs e) => _skipPath = e.Value?.ToString() ?? string.Empty;

    private void OnSourcePathInput(ChangeEventArgs e) => _sourcePath = e.Value?.ToString() ?? string.Empty;

    private void OnTargetPathInput(ChangeEventArgs e) => _targetPath = e.Value?.ToString() ?? string.Empty;

    private async Task PickSkipAsync()
    {
        var path = await PickFileAsync("Pick provider source to skip", s_sourceExtensions);
        if (!string.IsNullOrEmpty(path)) { _skipPath = path; }
    }

    private async Task PickSourceAsync()
    {
        var path = await PickFileAsync("Pick source (.db or .evtx)", s_sourceExtensions);
        if (!string.IsNullOrEmpty(path)) { _sourcePath = path; }
    }

    private async Task PickTargetAsync()
    {
        var path = await PickSaveFileAsync("Pick output .db (or type a new name)", s_dbExtensions, "providers.db");
        if (!string.IsNullOrEmpty(path)) { _targetPath = path; }
    }

    private Task RunElevatedAsync() =>
        base.RunElevatedAsync((request, logSink, ct) => ElevatedRunner.CreateAsync(request, logSink, progress: null, ct, VerboseLogging));
}
