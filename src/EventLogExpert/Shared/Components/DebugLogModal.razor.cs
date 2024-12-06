// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components;

public partial class DebugLogModal
{
    private readonly List<string> _data = [];

    [Inject] private IFileLogger TraceLogger { get; set; } = null!;

    protected internal override async Task Open()
    {
        await Refresh();

        await base.Open();
    }

    protected override void OnInitialized()
    {
        TraceLogger.DebugLogLoaded += () => Open().AndForget();

        base.OnInitialized();
    }

    private async Task Clear()
    {
        _data.Clear();

        await TraceLogger.ClearAsync();

        StateHasChanged();
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
