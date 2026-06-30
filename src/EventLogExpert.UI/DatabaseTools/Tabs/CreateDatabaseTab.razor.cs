// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.Eventing.PublisherMetadata.Offline;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Common.Versioning;
using EventLogExpert.Runtime.DatabaseTools.Elevation;
using Microsoft.AspNetCore.Components;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EventLogExpert.UI.DatabaseTools.Tabs;

public sealed partial class CreateDatabaseTab : DatabaseToolsTabBase<CreateDatabaseRequest>
{
    private static readonly IReadOnlyList<string> s_dbExtensions = [".db"];
    private static readonly IReadOnlyList<string> s_offlineImageExtensions = [".wim", ".esd", ".iso", ".vhdx", ".vhd"];
    private static readonly IReadOnlyList<string> s_skipExtensions = [".db", ".evtx"];
    private static readonly IReadOnlyList<string> s_sourceExtensions = [".db", ".evtx", ".wim", ".esd", ".iso", ".vhdx", ".vhd"];
    private static readonly IReadOnlyList<string> s_wimIndexExtensions = [".wim", ".esd", ".iso"];

    private Regex? _compiledFilter;
    private CancellationTokenSource? _editionsCts;
    private string? _editionsError;
    private string? _filterError;
    private string _filterText = string.Empty;
    private IReadOnlyList<WimImageEntry> _imageEditions = [];
    private bool _includeProtectedProviders;
    private bool _isLoadingEditions;
    private string? _overwriteConfirmedFor;
    private string _skipPath = string.Empty;
    private string _sourcePath = string.Empty;
    private string _targetPath = string.Empty;
    private bool _treatFolderAsImage;
    private int? _wimIndex;

    [CascadingParameter] internal IInlineAlertSurface? AlertSurface { get; set; }

    protected override bool CanRun => _filterError is null && !_isLoadingEditions && !string.IsNullOrWhiteSpace(_targetPath);

    protected override string? ProducedDatabasePathCandidate => _targetPath.Trim();

    [Inject] private ICurrentVersionProvider CurrentVersionProvider { get; init; } = null!;

    [Inject] private IElevatedDatabaseToolsRunner ElevatedRunner { get; init; } = null!;

    private string FilterCssClass => _filterError is null ? "input filter" : "input filter invalid";

    /// <summary>An empty source reads the live local providers (vs a .db/.evtx, folder, or offline-image source).</summary>
    private bool IsLocalProviderScan => string.IsNullOrWhiteSpace(_sourcePath);

    /// <summary>A folder the user explicitly marked as an offline image; read directly as a directory image.</summary>
    private bool IsMarkedFolderImage => _treatFolderAsImage && Directory.Exists(_sourcePath.Trim());

    /// <summary>
    ///     True when source is a .wim/.esd/.iso/.vhdx/.vhd file or a folder the user marked as an offline image (e.g. a
    ///     mounted volume).
    /// </summary>
    private bool IsOfflineImagePath =>
        !string.IsNullOrWhiteSpace(_sourcePath) &&
        (s_offlineImageExtensions.Contains(Path.GetExtension(_sourcePath.Trim()), StringComparer.OrdinalIgnoreCase) ||
            IsMarkedFolderImage);

    /// <summary>Only .wim/.esd/.iso take an image index; a folder or .vhdx/.vhd is read directly (no index).</summary>
    private bool IsWimIndexApplicable =>
        IsOfflineImagePath &&
        s_wimIndexExtensions.Contains(Path.GetExtension(_sourcePath.Trim()), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Tooltip and accessible description for the Run button when the click will prompt for elevation (the button
    ///     shows the UAC shield); <see langword="null" /> otherwise.
    /// </summary>
    private string? RunElevationTitle =>
        !WillElevate ? null :
        IsOfflineImagePath
            ? "Prompts for administrator access to read the offline image in an elevated helper; the app does not relaunch."
            : "Prompts for administrator access to include protected providers (Security, etc.); the app does not relaunch.";

    /// <summary>
    ///     The "include protected providers" choice is offered only for a non-elevated local-provider scan: protected
    ///     providers (Security, etc.) are skipped by the fast in-process scan and need the elevated helper to read. An admin
    ///     already reads them in-process, and a file or image source does not read live local providers, so the option is
    ///     hidden in those cases.
    /// </summary>
    private bool ShowProtectedProvidersOption => IsLocalProviderScan && !CurrentVersionProvider.IsAdmin;

    /// <summary>
    ///     True when the primary Run click will trigger a UAC prompt: a non-admin run that routes through the elevated
    ///     helper, either an offline image (extraction always needs admin) or a local scan where the user opted to include
    ///     protected providers. Drives both the Run button's shield icon and <see cref="RunCreateAsync" />'s dispatch, so the
    ///     shield shows exactly when (and only when) the click actually elevates.
    /// </summary>
    private bool WillElevate =>
        !CurrentVersionProvider.IsAdmin &&
        (IsOfflineImagePath || (IsLocalProviderScan && _includeProtectedProviders));

    public override void Dispose()
    {
        try { _editionsCts?.Cancel(); }
        catch (ObjectDisposedException) { }

        _editionsCts?.Dispose();
        _editionsCts = null;

        base.Dispose();
    }

    protected override CreateDatabaseRequest BuildRequest()
    {
        var source = string.IsNullOrWhiteSpace(_sourcePath) ? null : _sourcePath.Trim();
        var wimIndex = _wimIndex is > 0 ? _wimIndex : null;

        // Force Directory only for a user-marked folder; files (.wim/.esd/.iso/.vhdx/.vhd) auto-detect from the extension
        // so a .vhdx is read as Vhdx (auto-mount), not mistaken for a directory.
        var imageKind = IsMarkedFolderImage ? OfflineImageKind.Directory : (OfflineImageKind?)null;

        return new(
            _targetPath.Trim(),
            IsOfflineImagePath ? null : source,
            _compiledFilter,
            string.IsNullOrWhiteSpace(_skipPath) ? null : _skipPath.Trim(),
            OfflineImagePath: IsOfflineImagePath ? source : null,
            ImageKind: imageKind,
            WimIndex: IsWimIndexApplicable ? wimIndex : null,
            Overwrite: _overwriteConfirmedFor is not null && string.Equals(_overwriteConfirmedFor, _targetPath.Trim(), StringComparison.Ordinal));
    }

    /// <summary>
    ///     Prompts for confirmation before replacing an existing target database. Returns true (proceed) when the target
    ///     does not yet exist or the user confirms the overwrite; false (abort) when the user declines or no alert surface is
    ///     available to ask. The confirmed target is snapshotted so <see cref="BuildRequest" /> only sets <c>Overwrite</c>
    ///     when the path still matches - a target changed during the prompt does not silently carry it.
    /// </summary>
    protected override async Task<bool> ConfirmBeforeDispatchAsync()
    {
        _overwriteConfirmedFor = null;

        var target = _targetPath.Trim();

        if (string.IsNullOrEmpty(target) || !File.Exists(target)) { return true; }

        if (AlertSurface is null) { return false; }

        try
        {
            var result = await AlertSurface.ShowInlineAlertAsync(
                new InlineAlertRequest(
                    Title: "Database already exists",
                    Message: $"{Path.GetFileName(target)} already exists. Overwrite it? The existing database is backed up and restored automatically if the rebuild fails.",
                    AcceptLabel: "Overwrite",
                    CancelLabel: "Cancel",
                    IsPrompt: false,
                    PromptInitialValue: null),
                CancellationToken.None);

            if (!result.Accepted) { return false; }
        }
        catch (ObjectDisposedException) { return false; }

        _overwriteConfirmedFor = target;

        return true;
    }

    protected override Task<DatabaseToolsResult> DispatchAsync(
        CreateDatabaseRequest request,
        IProgress<LogRecord> logSink,
        CancellationToken cancellationToken) =>
        DatabaseToolsService.CreateAsync(request, logSink, progress: null, cancellationToken, VerboseLogging);

    /// <summary>The dropdown label for a single image edition, e.g. "4: ServerDataCenter (Windows Server 2025 ...)".</summary>
    private static string FormatEditionLabel(WimImageEntry edition) =>
        $"{edition.Index}: {edition.Edition} ({edition.Name})";

    /// <summary>
    ///     Display text for the selected image index. When the index matches a loaded edition the full edition label is
    ///     shown (so the collapsed box reads "4: ServerDataCenter (...)" instead of just "4"); otherwise the bare number is
    ///     shown (the user typed an index before, or without, loading the editions). The editable input falls back to the raw
    ///     number while focused, so this richer text never fights direct typing.
    /// </summary>
    private string FormatSelectedImageIndex(int? index)
    {
        if (index is null) { return string.Empty; }

        var match = _imageEditions.FirstOrDefault(edition => edition.Index == index);

        return match is null ? index.Value.ToString(CultureInfo.InvariantCulture) : FormatEditionLabel(match);
    }

    /// <summary>
    ///     Enumerates the source image's editions through the always-elevated helper and populates the image-index
    ///     dropdown. The user can still type an index directly (e.g. when they already know it); this button just fills the
    ///     list. Supersedes any in-flight load and is a no-op while a build is running.
    /// </summary>
    private async Task LoadEditionsAsync()
    {
        var imagePath = _sourcePath.Trim();

        if (IsRunning || _isLoadingEditions || string.IsNullOrEmpty(imagePath)) { return; }

        _editionsCts?.Cancel();
        _editionsCts?.Dispose();
        _editionsCts = new CancellationTokenSource();
        var token = _editionsCts.Token;

        _isLoadingEditions = true;
        _editionsError = null;
        _imageEditions = [];

        try
        {
            var logSink = new Progress<LogRecord>(AppendEntry);

            var result = await ElevatedRunner.ListImageEditionsAsync(
                new ListOfflineImageEditionsRequest(imagePath), logSink, token, VerboseLogging);

            if (token.IsCancellationRequested) { return; }

            if (result is { Outcome: DatabaseToolsOutcome.Succeeded, Editions: { Status: WimImageListStatus.Ok } editions })
            {
                _imageEditions = editions.Images;

                if (_imageEditions.Count == 0) { _editionsError = "No editions were found in the selected image."; }
            }
            else if (result.Outcome != DatabaseToolsOutcome.Cancelled)
            {
                _editionsError = result.FailureSummary ?? "Failed to list image editions.";
            }
        }
        catch (OperationCanceledException) { /* Superseded or cancelled; leave state for the newer request. */ }
        catch (Exception ex)
        {
            _editionsError = $"Failed to list image editions: {ex.Message}";
        }
        finally
        {
            if (!token.IsCancellationRequested) { _isLoadingEditions = false; }
        }
    }

    private void OnFilterInput(ChangeEventArgs e)
    {
        var pattern = e.Value?.ToString() ?? string.Empty;
        _filterText = pattern;

        _filterError = FilterRegexFactory.TryCreate(pattern, out _compiledFilter, out var error) ? null : $"Invalid regex: {error}";
    }

    private void OnIncludeProtectedProvidersInput(ChangeEventArgs e) => _includeProtectedProviders = e.Value as bool? ?? false;

    private void OnSkipPathInput(ChangeEventArgs e) => _skipPath = e.Value?.ToString() ?? string.Empty;

    private void OnSourcePathInput(ChangeEventArgs e) => SetSourcePath(e.Value?.ToString() ?? string.Empty);

    private void OnTargetPathInput(ChangeEventArgs e) => _targetPath = e.Value?.ToString() ?? string.Empty;

    private void OnTreatFolderAsImageInput(ChangeEventArgs e) => _treatFolderAsImage = e.Value as bool? ?? false;

    private async Task PickSkipAsync()
    {
        var path = await PickFileAsync("Pick provider source to skip", s_skipExtensions);
        if (!string.IsNullOrEmpty(path)) { _skipPath = path; }
    }

    private async Task PickSourceAsync()
    {
        var path = await PickFileAsync("Pick source (.db, .evtx, .wim, .esd, .iso, .vhdx, or .vhd)", s_sourceExtensions);
        if (!string.IsNullOrEmpty(path)) { SetSourcePath(path); }
    }

    private async Task PickTargetAsync()
    {
        var path = await PickSaveFileAsync("Pick output .db (or type a new name)", s_dbExtensions, "providers.db");
        if (!string.IsNullOrEmpty(path)) { _targetPath = path; }
    }

    /// <summary>
    ///     Run handler for the primary Run button. Routes through the elevated helper exactly when the click will trigger
    ///     a UAC prompt (<see cref="WillElevate" />): extracting from an offline image on a non-admin process (extraction
    ///     needs admin), or a non-admin local scan where the user opted to include protected providers (Security, etc.). All
    ///     other runs (already-admin, or a local scan that skips protected providers) use the in-process service. The Run
    ///     button shows the UAC shield in exactly the cases this routes to the elevated helper.
    /// </summary>
    private Task RunCreateAsync() => WillElevate ? RunElevatedAsync() : RunAsync();

    private Task RunElevatedAsync() =>
        base.RunElevatedAsync((request, logSink, ct) => ElevatedRunner.CreateAsync(request, logSink, progress: null, ct, VerboseLogging));

    /// <summary>Sets the source path; changing it clears any folder-as-image mark, image index, and loaded editions.</summary>
    private void SetSourcePath(string path)
    {
        if (_sourcePath.Trim() != path.Trim())
        {
            _treatFolderAsImage = false;
            _wimIndex = null;
            _imageEditions = [];
            _editionsError = null;

            _editionsCts?.Cancel();
            _editionsCts?.Dispose();
            _editionsCts = null;
            _isLoadingEditions = false;
        }

        _sourcePath = path;
    }
}
