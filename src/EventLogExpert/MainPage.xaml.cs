// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventResolvers;
using EventLogExpert.Store.EventLog;
using EventLogExpert.Store.Settings;
using EventLogExpert.Store.StatusBar;
using System.Diagnostics.Eventing.Reader;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert;

public partial class MainPage : ContentPage
{
    private readonly IDispatcher _fluxorDispatcher;

    private readonly IEventResolver _resolver;

    public MainPage(IDispatcher fluxorDispatcher, IEventResolver resolver)
    {
        InitializeComponent();

        _fluxorDispatcher = fluxorDispatcher;

        _resolver = resolver;

        PopulateOtherLogsMenu();

        ListenForResolverStatus();
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

    private async void PopulateOtherLogsMenu()
    {
        // Do this in the background to improve startup time.
        var names = await Task.Run(() =>
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

            return names;
        });

        foreach (var name in names)
        {
            var m = new MenuFlyoutItem { Text = name };
            m.Clicked += OpenLiveLog_Clicked;
            OtherLogsFlyoutSubitem.Add(m);
        }
    }

    private void ListenForResolverStatus()
    {
        _resolver.StatusChanged += (sender, status) =>
        {
            _fluxorDispatcher.Dispatch(new StatusBarAction.SetResolverStatus(status));
        };

        if (!string.IsNullOrEmpty(_resolver.Status))
        {
            _fluxorDispatcher.Dispatch(new StatusBarAction.SetResolverStatus(_resolver.Status));
        }
    }
}
