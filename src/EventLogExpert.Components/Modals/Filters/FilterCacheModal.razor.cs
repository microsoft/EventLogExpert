// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Components.Base;
using EventLogExpert.UI;
using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.Common.Files;
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

    [Inject] private IFilePickerService FilePickerService { get; init; } = null!;

    [Inject] private IFileSaveService FileSaveService { get; init; } = null!;

    [Inject] private IState<FilterCacheState> FilterCacheState { get; init; } = null!;

    protected override async Task OnExportAsync()
    {
        var snapshot = FilterCacheState.Value.FavoriteFilters;

        try
        {
            await FileSaveService.SaveAsync(
                "Saved Filters",
                FileSaveFileTypes.Json,
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
        try
        {
            var path = await FilePickerService.PickAsync(
                "Please select a json file to import",
                FilePickerFileTypes.Json);

            if (path is null) { return; }

            await using var stream = File.OpenRead(path);
            var filters = await JsonSerializer.DeserializeAsync<List<string>>(stream);

            if (filters is null) { return; }

            Dispatcher.Dispatch(new ImportFavoritesAction(filters));
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Import Failed",
                $"An exception occurred while importing filters: {ex.Message}",
                "OK");
        }
    }

    private void AddFavorite(string filter) => Dispatcher.Dispatch(new AddFavoriteFilterAction(filter));

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

        Dispatcher.Dispatch(new AddFilterAction(model));

        await CloseAsync();
    }

    private void RemoveFavorite(string filter) =>
        Dispatcher.Dispatch(new RemoveFavoriteFilterAction(filter));
}
