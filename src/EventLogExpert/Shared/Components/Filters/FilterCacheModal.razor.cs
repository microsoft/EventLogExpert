// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Windows.Storage.Pickers;
using WinRT.Interop;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterCacheModal
{
    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<FilterCacheState> FilterCacheState { get; init; } = null!;

    [Inject] private IFileLogger TraceLogger { get; init; } = null!;

    protected override void OnInitialized()
    {
        SubscribeToAction<FilterCacheAction.OpenMenu>(OnOpenMenu);

        base.OnInitialized();
    }

    private void AddFavorite(string filter) =>
        Dispatcher.Dispatch(new FilterCacheAction.AddFavoriteFilter(filter));

    private async Task AddFilter(string filter)
    {
        Dispatcher.Dispatch(
            new FilterPaneAction.AddFilter(
                new FilterModel
                {
                    Comparison = new FilterComparison { Value = filter },
                    FilterType = FilterType.Cached,
                    IsEnabled = true
                }));

        await Close();
    }

    private async Task ExportFavorites()
    {
        FileSavePicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "Saved Filters"
        };

        picker.FileTypeChoices.Add("JSON", [".json"]);

        if (Application.Current?.Windows[0].Handler?.PlatformView is not MauiWinUIWindow window)
        {
            return;
        }

        InitializeWithWindow.Initialize(picker, window.WindowHandle);

        var result = await picker.PickSaveFileAsync();

        if (result is null) { return; }

        try
        {
            using var stream = new MemoryStream(
                JsonSerializer.SerializeToUtf8Bytes(
                    FilterCacheState.Value.FavoriteFilters));

            await using var fileStream = await result.OpenStreamForWriteAsync();

            await stream.CopyToAsync(fileStream);
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Export Failed",
                $"An exception occurred while exporting filters: {ex.Message}",
                "OK");
        }
    }

    private async Task ImportFavorites()
    {
        PickOptions options = new()
        {
            PickerTitle = "Please select a json file to import",
            FileTypes = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, [".json"] }
                })
        };

        var result = await FilePicker.Default.PickAsync(options);

        if (result is null) { return; }

        try
        {
            await using var stream = File.OpenRead(result.FullPath);
            var filters = await JsonSerializer.DeserializeAsync<List<string>>(stream);

            if (filters is null) { return; }

            Dispatcher.Dispatch(new FilterCacheAction.ImportFavorites(filters));
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Import Failed",
                $"An exception occurred while importing filters: {ex.Message}",
                "OK");
        }
    }

    private async void OnOpenMenu(FilterCacheAction.OpenMenu action)
    {
        try
        {
            await InvokeAsync(Open);
        }
        catch (Exception e)
        {
            TraceLogger.Trace($"Failed to open filter cache modal: {e}", LogLevel.Error);
        }
    }

    private void RemoveFavorite(string filter) =>
        Dispatcher.Dispatch(new FilterCacheAction.RemoveFavoriteFilter(filter));
}
