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
    private bool _sourceIsExistingDirectory;
    private string _sourcePath = string.Empty;
    private string _targetPath = string.Empty;
    private bool _treatFolderAsImage;
    private int? _wimIndex;

    [CascadingParameter] internal IInlineAlertSurface? AlertSurface { get; set; }

    protected override bool CanRun => _filterError is null && !_isLoadingEditions && !string.IsNullOrWhiteSpace(_targetPath);

    protected override string LogCategory => LogCategories.DatabaseToolsCreate;

    protected override string? ProducedDatabasePathCandidate => _targetPath.Trim();

    [Inject] private ICurrentVersionProvider CurrentVersionProvider { get; init; } = null!;

    [Inject] private IElevatedDatabaseToolsRunner ElevatedRunner { get; init; } = null!;

    private string FilterCssClass => _filterError is null ? "input filter" : "input filter invalid";

    private bool IsLocalProviderScan => string.IsNullOrWhiteSpace(_sourcePath);

    private bool IsMarkedFolderImage => _treatFolderAsImage && _sourceIsExistingDirectory;

    private bool IsOfflineImagePath =>
        !string.IsNullOrWhiteSpace(_sourcePath) &&
        (s_offlineImageExtensions.Contains(Path.GetExtension(_sourcePath.Trim()), StringComparer.OrdinalIgnoreCase) ||
            IsMarkedFolderImage);

    private bool IsWimIndexApplicable =>
        IsOfflineImagePath &&
        s_wimIndexExtensions.Contains(Path.GetExtension(_sourcePath.Trim()), StringComparer.OrdinalIgnoreCase);

    private string? RunElevationTitle =>
        !WillElevate ? null :
        IsOfflineImagePath
            ? "Prompts for administrator access to read the offline image in an elevated helper; the app does not relaunch."
            : "Prompts for administrator access to include protected providers (Security, etc.); the app does not relaunch.";

    private bool ShowProtectedProvidersOption => IsLocalProviderScan && !CurrentVersionProvider.IsAdmin;

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

        // Only user-marked folders force Directory; image files must auto-detect from extension.
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

    // The confirmed overwrite target is snapshotted so edits during the prompt cannot inherit it.
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
        IProgress<LogRecord> logProgress,
        CancellationToken cancellationToken) =>
        DatabaseToolsService.CreateAsync(request, logProgress, progress: null, cancellationToken, VerboseLogging);

    private static string FormatEditionLabel(WimImageEntry edition) =>
        $"{edition.Index}: {edition.Edition} ({edition.Name})";

    private string FormatSelectedImageIndex(int? index)
    {
        if (index is null) { return string.Empty; }

        var match = _imageEditions.FirstOrDefault(edition => edition.Index == index);

        return match is null ? index.Value.ToString(CultureInfo.InvariantCulture) : FormatEditionLabel(match);
    }

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
            IProgress<LogRecord> logProgress = OperationLogProgressFactory.Create(new Progress<LogRecord>(AppendEntry), LogCategory, VerboseLogging);

            var result = await ElevatedRunner.ListImageEditionsAsync(
                new ListOfflineImageEditionsRequest(imagePath), logProgress, token, VerboseLogging);

            if (token.IsCancellationRequested) { return; }

            if (result is { Outcome: DatabaseToolsOutcome.Succeeded, Editions: { Status: WimImageListStatus.Ok } editions })
            {
                _imageEditions = editions.Images;

                if (_imageEditions.Count == 0)
                {
                    _editionsError = "No editions were found in the selected image.";
                }
                else if (_imageEditions.All(edition => edition.Index != _wimIndex))
                {
                    // A loaded matching index is preserved; otherwise show the first edition as visible confirmation.
                    _wimIndex = _imageEditions[0].Index;
                }
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

    // Directory.Exists can block for seconds on an unresponsive UNC path; the source box re-renders on every keystroke, so
    // the folder-existence check runs off the UI thread and only its cached result drives the markup.
    private async Task RefreshSourceIsDirectoryAsync(string path)
    {
        string trimmed = path.Trim();
        bool exists = !string.IsNullOrEmpty(trimmed) && await Task.Run(() => Directory.Exists(trimmed));

        if (_sourcePath != path || _sourceIsExistingDirectory == exists) { return; }

        _sourceIsExistingDirectory = exists;
        await InvokeAsync(StateHasChanged);
    }

    private Task RunCreateAsync() => WillElevate ? RunElevatedAsync() : RunAsync();

    private Task RunElevatedAsync() =>
        base.RunElevatedAsync((request, logProgress, ct) => ElevatedRunner.CreateAsync(request, logProgress, progress: null, ct, VerboseLogging));

    private void SetSourcePath(string path)
    {
        if (_sourcePath.Trim() != path.Trim())
        {
            _treatFolderAsImage = false;
            _wimIndex = null;
            _imageEditions = [];
            _editionsError = null;
            _sourceIsExistingDirectory = false;

            _editionsCts?.Cancel();
            _editionsCts?.Dispose();
            _editionsCts = null;
            _isLoadingEditions = false;
        }

        _sourcePath = path;
        _ = RefreshSourceIsDirectoryAsync(path);
    }
}
