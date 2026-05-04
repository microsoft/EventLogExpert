// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Components.Base;
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

    [Inject] private IFileSaveService FileSaveService { get; init; } = null!;

    [Inject] private IState<FilterGroupState> FilterGroupState { get; init; } = null!;

    protected override async Task OnExportAsync()
    {
        var snapshot = FilterGroupState.Value.Groups;

        try
        {
            await FileSaveService.SaveAsync(
                "Saved Groups",
                FileSaveServiceFileTypes.Json,
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
        PickOptions options = new()
        {
            PickerTitle = "Please select a json file to import",
            FileTypes = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>> { { DevicePlatform.WinUI, [".json"] } })
        };

        var result = await FilePicker.Default.PickAsync(options);

        if (result is null) { return; }

        try
        {
            await using var stream = File.OpenRead(result.FullPath);
            var groups = await JsonSerializer.DeserializeAsync<List<FilterGroupModel>>(stream);

            if (groups is null) { return; }

            Dispatcher.Dispatch(new FilterGroupAction.ImportGroups(groups));
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Import Failed",
                $"An exception occurred while importing groups: {ex.Message}",
                "OK");
        }
    }

    private void CreateGroup() =>
        Dispatcher.Dispatch(new FilterGroupAction.AddGroup(new FilterGroupModel { IsEditing = true }));
}
