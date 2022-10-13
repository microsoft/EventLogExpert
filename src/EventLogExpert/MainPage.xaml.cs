using EventLogExpert.Store;

namespace EventLogExpert;

public partial class MainPage : ContentPage
{
    private Fluxor.IDispatcher _fluxorDispatcher;

    public MainPage()
    {
        InitializeComponent();

        // this.Handler seems to be null even after InitializeComponent completes,
        // which means we can't access the IOC container to resolve the IDispatcher.
        // So here we launch a thread that just keeps trying to get the IDispatcher
        // until it does.
        Task.Run(() =>
        {
            while (_fluxorDispatcher == null)
            {
                _fluxorDispatcher = Handler?.MauiContext.Services.GetService<Fluxor.IDispatcher>();
                if (_fluxorDispatcher == null)
                {
                    Thread.Sleep(50);
                }
            }
        });
    }

    public async void OpenFile_Clicked(object sender, EventArgs e)
    {
        var options = new PickOptions();
        options.FileTypes = new FilePickerFileType(
            new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                        { DevicePlatform.WinUI, new[] { ".evtx" } }
                }
            );
        var result = await FilePicker.Default.PickAsync(options);
        if (result != null)
        {
            _fluxorDispatcher.Dispatch(
                new EventLogAction.OpenLog(
                    new EventLogState.LogSpecifier(
                        result.FullPath, Store.EventLogState.LogType.File)));
        }
    }

    private void OpenLiveLog_Clicked(object sender, EventArgs e)
    {
        _fluxorDispatcher.Dispatch(
                new EventLogAction.OpenLog(
                    new EventLogState.LogSpecifier(
                        "Application", Store.EventLogState.LogType.Live)));
    }
}
