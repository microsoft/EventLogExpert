// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Contracts;
using EventLogExpert.Runtime.Common.Elevation;
using EventLogExpert.Runtime.Common.Versioning;
using EventLogExpert.Runtime.Menu;
using Microsoft.AspNetCore.Components;
using System.Text.RegularExpressions;

namespace EventLogExpert.UI.DatabaseTools.Tabs;

public sealed partial class CreateDatabaseTab : DatabaseToolsTabBase<CreateDatabaseRequest>
{
    private static readonly IReadOnlyList<string> s_dbExtensions = [".db"];
    private static readonly TimeSpan s_filterCompileTimeout = TimeSpan.FromSeconds(1);
    private static readonly IReadOnlyList<string> s_sourceExtensions = [".db", ".evtx"];

    private Regex? _compiledFilter;
    private string? _elevationError;
    private string? _filterError;
    private string _filterText = string.Empty;
    private string _skipPath = string.Empty;
    private string _sourcePath = string.Empty;
    private string _targetPath = string.Empty;

    protected override bool CanRun => _filterError is null && !string.IsNullOrWhiteSpace(_targetPath);

    [Inject] private ICurrentVersionProvider CurrentVersionProvider { get; init; } = null!;

    [Inject] private IElevationService ElevationService { get; init; } = null!;

    private string FilterCssClass => _filterError is null ? "input filter" : "input filter invalid";

    [Inject] private IMenuActionService MenuActionService { get; init; } = null!;

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
        IProgress<DatabaseToolsLogEntry> logSink,
        CancellationToken cancellationToken) =>
        DatabaseToolsService.CreateAsync(request, logSink, progress: null, cancellationToken, VerboseLogging);

    private void OnFilterInput(ChangeEventArgs e)
    {
        var pattern = e.Value?.ToString() ?? string.Empty;
        _filterText = pattern;

        if (string.IsNullOrEmpty(pattern))
        {
            _compiledFilter = null;
            _filterError = null;

            return;
        }

        try
        {
            _compiledFilter = new Regex(pattern, RegexOptions.IgnoreCase, s_filterCompileTimeout);
            _filterError = null;
        }
        catch (ArgumentException ex)
        {
            _compiledFilter = null;
            _filterError = $"Invalid regex: {ex.Message}";
        }
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

    private async Task RelaunchAsAdminAsync()
    {
        _elevationError = null;

        var result = await ElevationService.RelaunchElevatedAsync();

        switch (result)
        {
            case ElevationResult.Relaunched:
                MenuActionService.Exit();
                break;
            case ElevationResult.UserCancelled:
                _elevationError = "Relaunch cancelled. Continuing without elevation.";
                break;
            case ElevationResult.Failed:
                _elevationError = "Relaunch failed. See debug log for details.";
                break;
        }
    }
}
