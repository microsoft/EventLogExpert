﻿// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.FilterPane;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text.Json;
using Windows.Storage.Pickers;
using WinRT.Interop;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterCacheModal
{
    [Inject] private IAlertDialogService AlertDialogService { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; set; } = null!;

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    protected override void OnInitialized()
    {
        SubscribeToAction<FilterCacheAction.OpenMenu>(action => Open().AndForget());

        base.OnInitialized();
    }

    private void AddFavorite(FilterModel filter) =>
        Dispatcher.Dispatch(new FilterCacheAction.AddFavoriteFilter(filter));

    private void AddFilter(FilterModel filter)
    {
        Dispatcher.Dispatch(new FilterPaneAction.AddCachedFilter(filter));
        Close().AndForget();
    }

    private async Task Close() => await JSRuntime.InvokeVoidAsync("closeFilterCacheModal");

    private async Task ExportFavorites()
    {
        FileSavePicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = "Saved Filters"
        };

        picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });

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
                    FilterCacheState.Value.FavoriteFilters.Select(x => x.Comparison.Value)));

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
            var json = await JsonSerializer.DeserializeAsync<List<string>>(stream);

            if (json is null) { return; }

            var filters = json
                .Select(x => new FilterModel { Comparison = new FilterComparison { Value = x } })
                .ToList();

            Dispatcher.Dispatch(new FilterCacheAction.ImportFavorites(filters));
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Import Failed",
                $"An exception occurred while importing filters: {ex.Message}",
                "OK");
        }
    }

    private async Task Open() => await JSRuntime.InvokeVoidAsync("openFilterCacheModal");

    private void RemoveFavorite(FilterModel filter) =>
        Dispatcher.Dispatch(new FilterCacheAction.RemoveFavoriteFilter(filter));
}
