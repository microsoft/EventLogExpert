// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using EventLogExpert.UI.Interfaces;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components;

public sealed partial class DebugLogModal : BaseModal<bool>
{
    private readonly List<string> _data = [];

    [Inject] private IFileLogger FileLogger { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        await Refresh();
        await base.OnInitializedAsync();
    }

    private async Task Clear()
    {
        _data.Clear();

        await FileLogger.ClearAsync();

        StateHasChanged();
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
