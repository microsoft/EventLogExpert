// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Components.Base;
using EventLogExpert.UI;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterCache;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using Microsoft.AspNetCore.Components;
using System.Text.Json;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Components.Modals.Filters;

public sealed partial class FilterCacheModal : ModalBase<bool>
{
    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IFileSaveService FileSaveService { get; init; } = null!;

    [Inject] private IState<FilterCacheState> FilterCacheState { get; init; } = null!;

    protected override async Task OnExportAsync()
    {
        var snapshot = FilterCacheState.Value.FavoriteFilters;

        try
        {
            await FileSaveService.SaveAsync(
                "Saved Filters",
                FileSaveServiceFileTypes.Json,
                stream => JsonSerializer.SerializeAsync(stream, snapshot));
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Export Failed",
                $"An exception occurred while exporting filters: {ex.Message}",
                "OK");
        }
    }

    protected override async Task OnImportAsync()
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

    private void AddFavorite(string filter) => Dispatcher.Dispatch(new FilterCacheAction.AddFavoriteFilter(filter));

    private async Task AddFilter(string filter)
    {
        var model = FilterModel.TryCreate(filter, FilterType.Cached, isEnabled: true);

        if (model is null)
        {
            await AlertDialogService.ShowAlert("Invalid Filter",
                $"The selected cached filter could not be parsed and will not be added:{Environment.NewLine}{Environment.NewLine}{filter}",
                "OK");

            return;
        }

        Dispatcher.Dispatch(new FilterPaneAction.AddFilter(model));

        await CloseAsync();
    }

    private void RemoveFavorite(string filter) =>
        Dispatcher.Dispatch(new FilterCacheAction.RemoveFavoriteFilter(filter));
}
