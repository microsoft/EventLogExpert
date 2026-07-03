// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.ShowProviders;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Versioning;
using EventLogExpert.Runtime.DatabaseTools.Elevation;
using Microsoft.AspNetCore.Components;
using System.Text.RegularExpressions;

namespace EventLogExpert.UI.DatabaseTools.Tabs;

public sealed partial class ShowProvidersTab : DatabaseToolsTabBase<ShowProvidersRequest>
{
    private static readonly IReadOnlyList<string> s_sourceExtensions = [".db", ".evtx"];

    private Regex? _compiledFilter;
    private string? _filterError;
    private string _filterText = string.Empty;
    private bool _includeProtectedProviders;
    private string _sourcePath = string.Empty;

    protected override bool CanRun => _filterError is null;

    protected override string LogCategory => LogCategories.DatabaseToolsShow;

    [Inject] private ICurrentVersionProvider CurrentVersionProvider { get; init; } = null!;

    [Inject] private IElevatedDatabaseToolsRunner ElevatedRunner { get; init; } = null!;

    private string FilterCssClass => _filterError is null ? "input filter" : "input filter invalid";

    private bool IsLocalProviderScan => string.IsNullOrWhiteSpace(_sourcePath);

    private string? RunElevationTitle =>
        WillElevate
            ? "Prompts for administrator access to include protected providers (Security, etc.); the app does not relaunch."
            : null;

    private bool ShowProtectedProvidersOption => IsLocalProviderScan && !CurrentVersionProvider.IsAdmin;

    private bool WillElevate =>
        !CurrentVersionProvider.IsAdmin && IsLocalProviderScan && _includeProtectedProviders;

    protected override ShowProvidersRequest BuildRequest()
    {
        var sourcePathTrimmed = string.IsNullOrWhiteSpace(_sourcePath) ? null : _sourcePath.Trim();

        return new ShowProvidersRequest(sourcePathTrimmed, _compiledFilter);
    }

    protected override Task<DatabaseToolsResult> DispatchAsync(
        ShowProvidersRequest request,
        IProgress<LogRecord> logProgress,
        CancellationToken cancellationToken) =>
        DatabaseToolsService.ShowAsync(request, logProgress, progress: null, cancellationToken, VerboseLogging);

    private void OnFilterInput(ChangeEventArgs e)
    {
        var pattern = e.Value?.ToString() ?? string.Empty;
        _filterText = pattern;

        _filterError = FilterRegexFactory.TryCreate(pattern, out _compiledFilter, out var error) ? null : $"Invalid regex: {error}";
    }

    private void OnIncludeProtectedProvidersInput(ChangeEventArgs e) => _includeProtectedProviders = e.Value as bool? ?? false;

    private async Task PickSourceAsync()
    {
        var path = await PickFileAsync("Pick source (.db or .evtx)", s_sourceExtensions);

        if (!string.IsNullOrEmpty(path)) { _sourcePath = path; }
    }

    private Task RunElevatedAsync() =>
        base.RunElevatedAsync((request, logProgress, ct) => ElevatedRunner.ShowAsync(request, logProgress, progress: null, ct, VerboseLogging));

    private Task RunShowAsync() => WillElevate ? RunElevatedAsync() : RunAsync();
}
