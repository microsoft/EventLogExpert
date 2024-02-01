// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store.FilterGroup;
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

    private void CreateGroup() =>
        Dispatcher.Dispatch(new FilterGroupAction.AddGroup(new FilterGroupModel { IsEditing = true }));

    private async Task Export()
    {
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

    private async Task Import()
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
}
