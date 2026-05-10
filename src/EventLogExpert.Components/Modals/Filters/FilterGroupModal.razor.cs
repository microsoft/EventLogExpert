// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Components.Base;
using EventLogExpert.UI.Common.Files;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterGroup;
using Fluxor;
using Microsoft.AspNetCore.Components;
using System.Text.Json;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Components.Modals.Filters;

public sealed partial class FilterGroupModal : ModalBase<bool>
{
    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IFilePickerService FilePickerService { get; init; } = null!;

    [Inject] private IFileSaveService FileSaveService { get; init; } = null!;

    [Inject] private IState<FilterGroupState> FilterGroupState { get; init; } = null!;

    protected override async Task OnExportAsync()
    {
        var snapshot = FilterGroupState.Value.Groups;

        try
        {
            await FileSaveService.SaveAsync(
                "Saved Groups",
                FileSaveFileTypes.Json,
                stream => JsonSerializer.SerializeAsync(stream, snapshot));
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Export Failed",
                $"An exception occurred while exporting saved groups: {ex.Message}",
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
            var groups = await JsonSerializer.DeserializeAsync<List<FilterGroupModel>>(stream);

            if (groups is null) { return; }

            Dispatcher.Dispatch(new ImportGroupsAction(groups));
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Import Failed",
                $"An exception occurred while importing groups: {ex.Message}",
                "OK");
        }
    }

    private void CreateGroup() =>
        Dispatcher.Dispatch(new AddGroupAction(new FilterGroupModel { IsEditing = true }));
}
