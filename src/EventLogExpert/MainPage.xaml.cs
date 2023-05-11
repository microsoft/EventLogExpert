// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Store.EventLog;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert;

public partial class MainPage : ContentPage
{
    private readonly IDispatcher _fluxorDispatcher;

    public MainPage(IDispatcher fluxorDispatcher)
    {
        InitializeComponent();

        _fluxorDispatcher = fluxorDispatcher;
    }

    public async void OpenFile_Clicked(object sender, EventArgs e)
    {
        var options = new PickOptions
        {
            FileTypes = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>> { { DevicePlatform.WinUI, new[] { ".evtx" } } }
            )
        };

        var result = await FilePicker.Default.PickAsync(options);

        if (result != null)
        {
            _fluxorDispatcher.Dispatch(
                new EventLogAction.OpenLog(
                    new EventLogState.LogSpecifier(
                        result.FullPath,
                        EventLogState.LogType.File)));
        }
    }

    private void OpenLiveLog_Clicked(object sender, EventArgs e)
    {
        _fluxorDispatcher.Dispatch(
            new EventLogAction.OpenLog(
                new EventLogState.LogSpecifier(
                    "Application",
                    EventLogState.LogType.Live)));
    }
}
