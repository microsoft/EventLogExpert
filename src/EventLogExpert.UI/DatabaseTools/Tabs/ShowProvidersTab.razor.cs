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

    [Inject] private ICurrentVersionProvider CurrentVersionProvider { get; init; } = null!;

    [Inject] private IElevatedDatabaseToolsRunner ElevatedRunner { get; init; } = null!;

    private string FilterCssClass => _filterError is null ? "input filter" : "input filter invalid";

    /// <summary>An empty source reads the live local providers (vs a .db or .evtx source).</summary>
    private bool IsLocalProviderScan => string.IsNullOrWhiteSpace(_sourcePath);

    /// <summary>
    ///     Tooltip and accessible description for the Run button when the click will prompt for elevation (the button
    ///     shows the UAC shield); <see langword="null" /> otherwise.
    /// </summary>
    private string? RunElevationTitle =>
        WillElevate
            ? "Prompts for administrator access to include protected providers (Security, etc.); the app does not relaunch."
            : null;

    /// <summary>
    ///     The "include protected providers" choice is offered only for a non-elevated local-provider scan: protected
    ///     providers (Security, etc.) are skipped by the fast in-process scan and need the elevated helper to read. An admin
    ///     already reads them in-process, and a .db or .evtx source does not read live local providers, so the option is
    ///     hidden in those cases.
    /// </summary>
    private bool ShowProtectedProvidersOption => IsLocalProviderScan && !CurrentVersionProvider.IsAdmin;

    /// <summary>
    ///     True when the primary Run click will trigger a UAC prompt: a non-admin local scan where the user opted to
    ///     include protected providers, which routes through the elevated helper. Drives both the Run button's shield icon and
    ///     <see cref="RunShowAsync" />'s dispatch, so the shield shows exactly when the click actually elevates.
    /// </summary>
    private bool WillElevate =>
        !CurrentVersionProvider.IsAdmin && IsLocalProviderScan && _includeProtectedProviders;

    protected override ShowProvidersRequest BuildRequest()
    {
        var sourcePathTrimmed = string.IsNullOrWhiteSpace(_sourcePath) ? null : _sourcePath.Trim();

        return new ShowProvidersRequest(sourcePathTrimmed, _compiledFilter);
    }

    protected override Task<DatabaseToolsResult> DispatchAsync(
        ShowProvidersRequest request,
        IProgress<LogRecord> logSink,
        CancellationToken cancellationToken) =>
        DatabaseToolsService.ShowAsync(request, logSink, progress: null, cancellationToken, VerboseLogging);

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
        base.RunElevatedAsync((request, logSink, ct) => ElevatedRunner.ShowAsync(request, logSink, progress: null, ct, VerboseLogging));

    /// <summary>
    ///     Run handler for the primary Run button. Routes through the elevated helper exactly when the click will trigger
    ///     a UAC prompt (<see cref="WillElevate" />): a non-admin local scan where the user opted to include protected
    ///     providers (Security, etc.). All other runs use the in-process service. The Run button shows the UAC shield in
    ///     exactly the cases this routes to the elevated helper.
    /// </summary>
    private Task RunShowAsync() => WillElevate ? RunElevatedAsync() : RunAsync();
}
