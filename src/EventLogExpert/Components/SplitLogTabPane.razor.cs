// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Store.EventLog;
using Fluxor;
using Microsoft.AspNetCore.Components;
using System.Collections.Immutable;
using static EventLogExpert.Store.EventLog.EventLogState;

namespace EventLogExpert.Components;

public partial class SplitLogTabPane
{
    [Parameter] public string? ActiveLog { get; set; }

    [Parameter] public EventCallback<string?> ActiveLogChanged { get; set; }

    [Inject] public IStateSelection<EventLogState, ImmutableDictionary<string, EventLogData>>
        ActiveLogState { get; set; } = null!;

    protected override void OnInitialized()
    {
        ActiveLogState.Select(e => e.ActiveLogs);

        ActiveLogState.SelectedValueChanged += async (sender, activeLog) =>
        {
            if (activeLog == ImmutableDictionary<string, EventLogData>.Empty) { await SetActiveLog(null); }
        };

        base.OnInitialized();
    }

    private static string GetTabName(string path) => Path.GetFileNameWithoutExtension(path);

    private async Task SetActiveLog(string? activeLog)
    {
        ActiveLog = activeLog;
        await ActiveLogChanged.InvokeAsync(ActiveLog);
    }
}
