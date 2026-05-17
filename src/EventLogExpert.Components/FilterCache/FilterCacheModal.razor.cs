// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Components.Base;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.FilterCache;
using Fluxor;
using Microsoft.AspNetCore.Components;
using System.Text.Json;

namespace EventLogExpert.Components.FilterCache;

public sealed partial class FilterCacheModal : ModalBase<bool>
{
    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IFilePickerService FilePickerService { get; init; } = null!;

    [Inject] private IFileSaveService FileSaveService { get; init; } = null!;

    [Inject] private IFilterCacheCommands FilterCacheCommands { get; init; } = null!;

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

            FilterCacheCommands.ImportFavorites(filters);
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Import Failed",
                $"An exception occurred while importing filters: {ex.Message}",
                "OK");
        }
    }

    private void AddFavorite(string filter) => FilterCacheCommands.AddFavoriteFilter(filter);

    private void RemoveFavorite(string filter) => FilterCacheCommands.RemoveFavoriteFilter(filter);
}
