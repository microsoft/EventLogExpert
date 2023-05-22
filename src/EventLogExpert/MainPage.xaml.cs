// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventResolvers;
using EventLogExpert.Store.EventLog;
using EventLogExpert.Store.Settings;
using Microsoft.AspNetCore.Components;
using System.Diagnostics.Eventing.Reader;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert;

public partial class MainPage : ContentPage
{
    private readonly IDispatcher _fluxorDispatcher;

    [Inject] private IEventResolver _resolver { get; init; } = null!;

    public MainPage(IDispatcher fluxorDispatcher)
    {
        InitializeComponent();

        _fluxorDispatcher = fluxorDispatcher;

        PopulateOtherLogsMenu();
    }

    public async void OpenFile_Clicked(object sender, EventArgs e)
    {
        var options = new PickOptions
        {
            FileTypes = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>> { { DevicePlatform.WinUI, new[] { ".evtx" } } }
            )
        };

        FileResult? result = await FilePicker.Default.PickAsync(options);

        if (result != null)
        {
            _fluxorDispatcher.Dispatch(
                new EventLogAction.OpenLog(
                    new EventLogState.LogSpecifier(
                        result.FullPath,
                        EventLogState.LogType.File)));
        }

        Utils.UpdateAppTitle(result?.FullPath);
    }

    private void CheckForUpdates_Clicked(object? sender, EventArgs e) =>
        _fluxorDispatcher.Dispatch(new SettingsAction.CheckForUpdates());

    private void ContinuouslyUpdate_Clicked(object sender, EventArgs e)
    {
        if (((MenuFlyoutItem)sender).Text == "Continuously Update")
        {
            _fluxorDispatcher.Dispatch(new EventLogAction.SetContinouslyUpdate(true));
            ((MenuFlyoutItem)sender).Text = "Continuously Update ✓";
        }
        else
        {
            _fluxorDispatcher.Dispatch(new EventLogAction.SetContinouslyUpdate(false));
            ((MenuFlyoutItem)sender).Text = "Continuously Update";
        }
    }

    private void LoadNewEvents_Clicked(object sender, EventArgs e)
    {
        _fluxorDispatcher.Dispatch(new EventLogAction.LoadNewEvents());
    }

    private void OpenLiveLog_Clicked(object? sender, EventArgs e)
    {
        if (sender == null) return;

        _fluxorDispatcher.Dispatch(
            new EventLogAction.OpenLog(
                new EventLogState.LogSpecifier(
                    ((MenuFlyoutItem)sender).Text,
                    EventLogState.LogType.Live)));

        Utils.UpdateAppTitle(((MenuFlyoutItem)sender).Text);
    }

    private void OpenSettingsModal_Clicked(object sender, EventArgs e) =>
        _fluxorDispatcher.Dispatch(new SettingsAction.OpenMenu());

    private void PopulateOtherLogsMenu()
    {
        var logsThatAlreadyHaveMenuItems = new[] { "Application", "System" };
        var session = new EventLogSession();
        var names = session.GetLogNames()
            .Where(n => !logsThatAlreadyHaveMenuItems.Contains(n))
            .OrderBy(n => n)
            .Where(n =>
            {
                try
                {
                    return session.GetLogInformation(n, PathType.LogName).CreationTime.HasValue;
                }
                catch
                {
                    return false;
                }
            });

        foreach (var name in names)
        {
            var m = new MenuFlyoutItem { Text = name };
            m.Clicked += OpenLiveLog_Clicked;
            OtherLogsFlyoutSubitem.Add(m);
        }
    }
}
