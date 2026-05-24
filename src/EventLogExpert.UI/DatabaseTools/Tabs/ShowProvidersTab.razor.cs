// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Contracts;
using Microsoft.AspNetCore.Components;
using System.Text.RegularExpressions;

namespace EventLogExpert.UI.DatabaseTools.Tabs;

public sealed partial class ShowProvidersTab : DatabaseToolsTabBase<ShowProvidersRequest>
{
    private static readonly TimeSpan s_filterCompileTimeout = TimeSpan.FromSeconds(1);
    private static readonly IReadOnlyList<string> s_sourceExtensions = [".db", ".evtx"];

    private Regex? _compiledFilter;
    private string? _filterError;
    private string _filterText = string.Empty;
    private string _sourcePath = string.Empty;

    protected override bool CanRun => _filterError is null;

    private string FilterCssClass => _filterError is null ? "input filter" : "input filter invalid";

    protected override ShowProvidersRequest BuildRequest()
    {
        var sourcePathTrimmed = string.IsNullOrWhiteSpace(_sourcePath) ? null : _sourcePath.Trim();

        return new ShowProvidersRequest(sourcePathTrimmed, _compiledFilter);
    }

    protected override Task<DatabaseToolsResult> DispatchAsync(
        ShowProvidersRequest request,
        IProgress<DatabaseToolsLogEntry> logSink,
        CancellationToken cancellationToken) =>
        DatabaseToolsService.ShowAsync(request, logSink, progress: null, cancellationToken, VerboseLogging);

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

    private async Task PickSourceAsync()
    {
        var path = await PickFileAsync("Pick source (.db or .evtx)", s_sourceExtensions);

        if (!string.IsNullOrEmpty(path)) { _sourcePath = path; }
    }
}
