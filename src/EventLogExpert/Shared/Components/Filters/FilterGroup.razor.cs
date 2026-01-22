// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.FilterGroup;
using EventLogExpert.UI.Store.FilterPane;
using Microsoft.AspNetCore.Components;
using System.Text.Json;
using Windows.Storage.Pickers;
using WinRT.Interop;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterGroup
{
    [Parameter] public FilterGroupModel Group { get; set; } = null!;

    [Parameter] public FilterGroupModal Parent { get; set; } = null!;

    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    private void AddFilter() => Dispatcher.Dispatch(new FilterGroupAction.AddFilter(Group.Id));

    private void ApplyFilters()
    {
        Dispatcher.Dispatch(new FilterPaneAction.ApplyFilterGroup(Group));
        Parent.Close().AndForget();
    }

    private void CopyGroup() => Clipboard.SetTextAsync(Group.Filters.Count() > 1 ?
        string.Join(" || ", Group.Filters.Select(filter => $"({filter.Comparison.Value})")) :
        Group.Filters.First().Comparison.Value);

    private async Task ExportGroup()
    {
        FileSavePicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Group.DisplayName
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
            using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(Group));

            await using var fileStream = await result.OpenStreamForWriteAsync();

            await stream.CopyToAsync(fileStream);
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Export Failed",
                $"An exception occurred while exporting saved groups: {ex.Message}",
                "OK");
        }
    }

    private async Task ImportGroup()
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
            var group = await JsonSerializer.DeserializeAsync<FilterGroupModel>(stream);

            if (group is null) { return; }

            var updatedGroup = Group with
            {
                Name = group.Name,
                Filters = group.Filters
            };

            Dispatcher.Dispatch(new FilterGroupAction.SetGroup(updatedGroup));
        }
        catch (Exception ex)
        {
            await AlertDialogService.ShowAlert("Import Failed",
                $"An exception occurred while importing group: {ex.Message}",
                "OK");
        }
    }

    private void RemoveGroup() => Dispatcher.Dispatch(new FilterGroupAction.RemoveGroup(Group.Id));

    private async Task RenameGroup()
    {
        var newName = await AlertDialogService.DisplayPrompt("Group Name", "What would you like to name this group?", Group.Name);

        if (string.IsNullOrEmpty(newName))
        {
            await AlertDialogService.ShowAlert("Rename Failed", "Name cannot be empty", "OK");

            return;
        }

        if (string.Equals(newName, Group.Name))
        {
            await AlertDialogService.ShowAlert("Rename Failed", "Name cannot be the same as previous name", "OK");

            return;
        }

        Dispatcher.Dispatch(new FilterGroupAction.SetGroup(Group with { Name = newName }));
    }

    private void SaveGroup()
    {
        foreach (var filter in Group.Filters)
        {
            if (filter.IsEditing) { return; }
        }

        Dispatcher.Dispatch(new FilterGroupAction.SetGroup(Group));
    }

    private void ToggleGroup() => Dispatcher.Dispatch(new FilterGroupAction.ToggleGroup(Group.Id));
}
