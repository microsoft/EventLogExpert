// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Adapters.Menu;
using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Activation;
using EventLogExpert.Runtime.Common.AppTitle;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Settings;
using Fluxor;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Web.WebView2.Core;
using System.Collections.Immutable;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using DataPackageOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation;
using DragEventArgs = Microsoft.Maui.Controls.DragEventArgs;

namespace EventLogExpert;

public sealed partial class MainPage : ContentPage, IDisposable
{
    private readonly IActivationDispatcher _activationDispatcher;
    private readonly IAppTitleService _appTitleService;
    private readonly CancellationTokenSource _consumerCts = new();
    private readonly IStateSelection<LogTableState, ImmutableList<LogView>> _logTables;
    private readonly MauiMenuActionService _menuActionService;
    private readonly ISettingsService _settings;
    private readonly ITraceLogger _traceLogger;

    private CoreWebView2? _coreWebView;
    private bool _disposed;

    public MainPage(
        IFilterLibraryCommands filterLibraryCommands,
        ILogTableCommands logTableCommands,
        IStateSelection<LogTableState, ImmutableList<LogView>> logTables,
        ISettingsService settings,
        IAppTitleService appTitleService,
        ITraceLogger traceLogger,
        MauiMenuActionService menuActionService,
        IActivationDispatcher activationDispatcher)
    {
        InitializeComponent();

        _logTables = logTables;
        _appTitleService = appTitleService;
        _settings = settings;
        _traceLogger = traceLogger;
        _menuActionService = menuActionService;
        _activationDispatcher = activationDispatcher;

        _logTables.Select(state => state.EventTables);
        _logTables.SelectedValueChanged += OnLogTablesChanged;

        _settings.ThemeChanged += OnThemeChanged;

        logTableCommands.LoadColumns();
        filterLibraryCommands.LoadLibrary();

        // Eager subscription so cold-launch args buffered by ActivationBootstrap drain even when
        // WebView2 is missing and BlazorWebViewInitialized never fires; the StartConsumingAsync
        // call in BlazorWebViewInitialized is idempotent (guarded by Interlocked) so a second
        // call from there is safe.
        _ = _activationDispatcher.StartConsumingAsync(
            _menuActionService.OpenLogsBatchAsync,
            _consumerCts.Token);
    }

    public void Dispose()
    {
        _disposed = true;

        _logTables.SelectedValueChanged -= OnLogTablesChanged;
        _settings.ThemeChanged -= OnThemeChanged;

        try { _consumerCts.Cancel(); }
        catch (ObjectDisposedException) { /* already disposed */ }

        _consumerCts.Dispose();
    }

    private void ApplyWebViewTheme(Theme theme)
    {
        // Keep WebView2's prefers-color-scheme aligned with the user's choice so the "System" path
        // (data-theme attribute removed) actually follows the OS, and so explicit Light/Dark stays
        // consistent across the page.
        _coreWebView?.Profile.PreferredColorScheme = theme switch
        {
            Theme.Light => CoreWebView2PreferredColorScheme.Light,
            Theme.Dark => CoreWebView2PreferredColorScheme.Dark,
            _ => CoreWebView2PreferredColorScheme.Auto,
        };
    }

    private async void DropGestureRecognizer_OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.PlatformArgs is null) { return; }

        if (!e.PlatformArgs.DragEventArgs.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.PlatformArgs.DragEventArgs.AcceptedOperation = DataPackageOperation.None;
        }

        var deferral = e.PlatformArgs.DragEventArgs.GetDeferral();
        bool isAllowed = false;
        IReadOnlyList<IStorageItem> items = await e.PlatformArgs.DragEventArgs.DataView.GetStorageItemsAsync();

        foreach (var item in items)
        {
            if (item is not StorageFile file ||
                !string.Equals(file.FileType, ".evtx", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            isAllowed = true;
            break;
        }

        e.PlatformArgs.DragEventArgs.AcceptedOperation = isAllowed
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;

        deferral.Complete();
    }

    private async void DropGestureRecognizer_OnDrop(object? sender, DropEventArgs e)
    {
        if (e.PlatformArgs is null ||
            !e.PlatformArgs.DragEventArgs.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        IReadOnlyList<IStorageItem> items = await e.PlatformArgs.DragEventArgs.DataView.GetStorageItemsAsync();

        var droppedFilePaths = items.OfType<StorageFile>()
            .Where(file => !string.IsNullOrEmpty(file.Path))
            .Select(file => (file.Path, LogPathType.File))
            .ToList();

        if (droppedFilePaths.Count == 0) { return; }

        await _menuActionService.OpenLogsBatchAsync(droppedFilePaths, combineLog: true);
    }

    private void MainWebView_BlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
    {
        _coreWebView = e.WebView.CoreWebView2;

        _ = _activationDispatcher.StartConsumingAsync(
            _menuActionService.OpenLogsBatchAsync,
            _consumerCts.Token);

        if (_coreWebView is null) { return; }

        ApplyWebViewTheme(_settings.Theme);

        var themeAttr = _settings.Theme switch
        {
            Theme.Light => "light",
            Theme.Dark => "dark",
            _ => null,
        };

        var script = themeAttr is null
            ? "document.documentElement.removeAttribute('data-theme');"
            : $"document.documentElement.setAttribute('data-theme','{themeAttr}');";

        _ = _coreWebView.AddScriptToExecuteOnDocumentCreatedAsync(script);
    }

    private void OnLogTablesChanged(object? sender, ImmutableList<LogView> eventTables)
    {
        if (_disposed) { return; }

        MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (_disposed) { return; }

            var logNames = eventTables.Where(table => !table.IsCombined).Select(table => table.LogName).ToList();

            _appTitleService.SetLogName(logNames.Count <= 0 ? null : string.Join(" | ", logNames));
        });
    }

    private void OnThemeChanged() =>
        // ThemeChanged may be raised from non-UI threads; marshal to the UI thread before touching
        // WebView2's profile.
        MainThread.BeginInvokeOnMainThread(() => ApplyWebViewTheme(_settings.Theme));
}
