// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components;

public partial class DebugLogModal : IDisposable
{
    private readonly List<string> _data = [];

    [Inject] private IFileLogger FileLogger { get; set; } = null!;

    [Inject] private ITraceLogger TraceLogger { get; set; } = null!;

    public void Dispose()
    {
        FileLogger.DebugLogLoaded -= OnDebugLogLoaded;
        GC.SuppressFinalize(this);
    }

    protected internal override async Task Open()
    {
        await Refresh();

        await base.Open();
    }

    protected override void OnInitialized()
    {
        FileLogger.DebugLogLoaded += OnDebugLogLoaded;

        base.OnInitialized();
    }

    private async Task Clear()
    {
        _data.Clear();

        await FileLogger.ClearAsync();

        StateHasChanged();
    }

    private async void OnDebugLogLoaded()
    {
        try
        {
            await InvokeAsync(Open);
        }
        catch (Exception e)
        {
            TraceLogger.Error($"Failed to open debug log modal: {e}");
        }
    }

    private async Task Refresh()
    {
        _data.Clear();

        await foreach (var line in FileLogger.LoadAsync())
        {
            _data.Add(line);
        }

        StateHasChanged();
    }
}
