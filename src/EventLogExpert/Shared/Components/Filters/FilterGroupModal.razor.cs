// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.FilterGroup;
using EventLogExpert.UI.Store.FilterPane;
using Fluxor;
using Microsoft.AspNetCore.Components;
using System.Text.Json;
using Windows.Storage.Pickers;
using WinRT.Interop;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterGroupModal
{
    [Inject] private IAlertDialogService AlertDialogService { get; init; } = null!;

    [Inject] private IDispatcher Dispatcher { get; init; } = null!;

    [Inject] private IState<FilterGroupState> FilterGroupState { get; init; } = null!;

    protected override void OnInitialized()
    {
        SubscribeToAction<FilterGroupAction.OpenMenu>(action => Open().AndForget());

        base.OnInitialized();
    }

    private void AddFilter(FilterGroupModel group) => Dispatcher.Dispatch(new FilterGroupAction.AddFilter(group.Id));

    private void ApplyFilters(FilterGroupModel group)
    {
        Dispatcher.Dispatch(new FilterPaneAction.ApplyFilterGroup(group));
        Close().AndForget();
    }

    private void CopyGroup(Guid id)
    {
        var group = FilterGroupState.Value.Groups.FirstOrDefault(g => g.Id == id);

        if (group is null) { return; }

        Clipboard.SetTextAsync(group.Filters.Count() > 1 ?
            string.Join(" || ", group.Filters.Select(filter => $"({filter.Comparison.Value})")) :
            group.Filters.First().Comparison.Value);
    }

    private void CreateGroup() =>
        Dispatcher.Dispatch(new FilterGroupAction.AddGroup(new FilterGroupModel { IsEditing = true }));

    private async Task Export() {
        FileSavePicker picker = new()
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "Saved Groups"
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
                    FilterGroupState.Value.Groups));

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

    private async Task Import() {
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

    private void RemoveGroup(Guid id) => Dispatcher.Dispatch(new FilterGroupAction.RemoveGroup(id));

    private async void RenameGroup(FilterGroupModel model)
    {
        var groupName = await AlertDialogService.DisplayPrompt("Group Name", "What would you like to name this group?");

        if (string.IsNullOrEmpty(groupName)) { return; }

        model.Name = groupName;

        Dispatcher.Dispatch(new FilterGroupAction.SetGroup(model));
    }

    private void SaveGroup(FilterGroupModel group)
    {
        foreach (var filter in group.Filters)
        {
            if (filter.IsEditing) { return; }
        }

        Dispatcher.Dispatch(new FilterGroupAction.SetGroup(group));
    }

    private void ToggleGroup(Guid id) => Dispatcher.Dispatch(new FilterGroupAction.ToggleGroup(id));
}
