// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Services;
using EventLogExpert.UI;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Options;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.EventLog;
using EventLogExpert.UI.Store.EventTable;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.FilterGroup;
using Fluxor;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Web.WebView2.Core;
using System.Collections.Immutable;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using DataPackageOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation;
using DragEventArgs = Microsoft.Maui.Controls.DragEventArgs;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert;

public sealed partial class MainPage : ContentPage, IDisposable
{
    private readonly IStateSelection<EventLogState, ImmutableDictionary<string, EventLogData>> _activeLogs;
    private readonly IAppTitleService _appTitleService;
    private readonly IDatabaseCollectionProvider _databaseCollectionProvider;
    private readonly IDatabaseService _databaseService;
    private readonly FileLocationOptions _fileLocationOptions;
    private readonly MauiMenuActionService _menuActionService;
    private readonly ISettingsService _settings;
    private readonly ITraceLogger _traceLogger;

    private CoreWebView2? _coreWebView;
    private bool _disposed;

    public MainPage(
        IDispatcher fluxorDispatcher,
        IDatabaseCollectionProvider databaseCollectionProvider,
        IStateSelection<EventLogState, ImmutableDictionary<string, EventLogData>> activeLogs,
        IDatabaseService databaseService,
        ISettingsService settings,
        IAppTitleService appTitleService,
        FileLocationOptions fileLocationOptions,
        ITraceLogger traceLogger,
        MauiMenuActionService menuActionService)
    {
        InitializeComponent();

        _activeLogs = activeLogs;
        _appTitleService = appTitleService;
        _databaseCollectionProvider = databaseCollectionProvider;
        _databaseService = databaseService;
        _fileLocationOptions = fileLocationOptions;
        _settings = settings;
        _traceLogger = traceLogger;
        _menuActionService = menuActionService;

        _activeLogs.Select(state => state.ActiveLogs);

        _activeLogs.SelectedValueChanged += OnActiveLogsChanged;
        _databaseService.LoadedDatabasesChanged += OnLoadedDatabasesChanged;
        _settings.ThemeChanged += OnThemeChanged;

        fluxorDispatcher.Dispatch(new EventTableAction.LoadColumns());
        fluxorDispatcher.Dispatch(new FilterCacheAction.LoadFilters());
        fluxorDispatcher.Dispatch(new FilterGroupAction.LoadGroups());

        _ = ProcessCommandLine();
    }

    public void Dispose()
    {
        _disposed = true;
        _activeLogs.SelectedValueChanged -= OnActiveLogsChanged;
        _databaseService.LoadedDatabasesChanged -= OnLoadedDatabasesChanged;
        _settings.ThemeChanged -= OnThemeChanged;
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

        foreach (var item in items)
        {
            if (item is not StorageFile file || _activeLogs.Value.ContainsKey(file.Path))
            {
                continue;
            }

            await _menuActionService.OpenLogAsync(file.Path, PathType.FilePath, true);
        }
    }

    private void MainWebView_BlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
    {
        // Inject the saved theme synchronously into every document the WebView2 creates so the page
        // renders with the correct palette on first paint (avoids the dark→light flash while
        // Blazor boots and calls setTheme). Also align WebView2's prefers-color-scheme with the
        // saved choice so the System theme path lights up the right CSS branch immediately.
        _coreWebView = e.WebView.CoreWebView2;

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

    private void OnActiveLogsChanged(object? sender, ImmutableDictionary<string, EventLogData> updatedActiveLogs)
    {
        if (_disposed) { return; }

        MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (_disposed) { return; }

            _appTitleService.SetLogName(
                updatedActiveLogs.Count <= 0
                    ? null
                    : string.Join(" | ", updatedActiveLogs.Values.Select(log => log.Name)));
        });
    }

    private void OnLoadedDatabasesChanged(object? sender, IEnumerable<string> loadedProviders)
    {
        if (_disposed) { return; }

        _databaseCollectionProvider.SetActiveDatabases(loadedProviders.Select(path =>
            Path.Join(_fileLocationOptions.DatabasePath, path)));
    }

    private void OnThemeChanged() =>
        // ThemeChanged may be raised from non-UI threads; marshal to the UI thread before touching
        // WebView2's profile.
        MainThread.BeginInvokeOnMainThread(() => ApplyWebViewTheme(_settings.Theme));

    private async Task ProcessCommandLine()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();

            foreach (var arg in args)
            {
                if (arg.EndsWith(".evtx", StringComparison.OrdinalIgnoreCase))
                {
                    await _menuActionService.OpenLogAsync(arg, PathType.FilePath);
                }
            }
        }
        catch (Exception e)
        {
            _traceLogger.Error($"Failed to process command line arguments: {e}");
        }
    }
}
