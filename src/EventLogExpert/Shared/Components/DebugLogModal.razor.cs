// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using EventLogExpert.UI.Interfaces;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components;

public sealed partial class DebugLogModal : ModalBase<bool>
{
    private const int RenderBatchSize = 100;

    private readonly List<string> _data = [];

    private bool _hasLoaded;
    private int _loadGeneration;

    [Inject] private IFileLogger FileLogger { get; set; } = null!;

    protected override async ValueTask DisposeAsyncCore(bool disposing)
    {
        if (disposing)
        {
            _loadGeneration++;
        }

        await base.DisposeAsyncCore(disposing);
    }

    protected override async Task OnInitializedAsync()
    {
        await Refresh();

        await base.OnInitializedAsync();
    }

    private async Task Clear()
    {
        _loadGeneration++;
        _data.Clear();
        _hasLoaded = true;

        await FileLogger.ClearAsync();

        StateHasChanged();
    }

    private async Task Refresh()
    {
        var generation = ++_loadGeneration;

        _hasLoaded = false;
        _data.Clear();
        StateHasChanged();

        var sinceRender = 0;

        try
        {
            await foreach (var line in FileLogger.LoadAsync())
            {
                if (generation != _loadGeneration) { return; }

                _data.Add(line);

                if (++sinceRender < RenderBatchSize) { continue; }

                sinceRender = 0;

                StateHasChanged();
                
                await Task.Yield();
            }
        }
        finally
        {
            if (generation == _loadGeneration)
            {
                _hasLoaded = true;

                StateHasChanged();
            }
        }
    }
}
