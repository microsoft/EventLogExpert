// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Shared.Components;

public partial class DebugLogModal : IDisposable
{
    private readonly List<string> _data = [];

    [Inject] private IFileLogger TraceLogger { get; set; } = null!;

    public void Dispose()
    {
        TraceLogger.DebugLogLoaded -= OnDebugLogLoaded;
        GC.SuppressFinalize(this);
    }

    protected internal override async Task Open()
    {
        await Refresh();

        await base.Open();
    }

    protected override void OnInitialized()
    {
        TraceLogger.DebugLogLoaded += OnDebugLogLoaded;

        base.OnInitialized();
    }

    private async Task Clear()
    {
        _data.Clear();

        await TraceLogger.ClearAsync();

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
            TraceLogger.Trace($"Failed to open debug log modal: {e}", LogLevel.Error);
        }
    }

    private async Task Refresh()
    {
        _data.Clear();

        await foreach (var line in TraceLogger.LoadAsync())
        {
            _data.Add(line);
        }

        StateHasChanged();
    }
}
