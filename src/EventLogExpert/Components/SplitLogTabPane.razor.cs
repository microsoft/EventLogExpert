// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Store.EventLog;
using Fluxor;
using Microsoft.AspNetCore.Components;
using System.Collections.Immutable;
using static EventLogExpert.UI.Store.EventLog.EventLogState;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert.Components;

public partial class SplitLogTabPane
{
    [Parameter] public string? ActiveLog { get; set; }

    [Parameter] public EventCallback<string?> ActiveLogChanged { get; set; }

    [Inject] public IStateSelection<EventLogState, ImmutableDictionary<string, EventLogData>>
        ActiveLogState { get; set; } = null!;

    [Inject] private IDispatcher Dispatcher { get; set; } = null!;

    protected override void OnInitialized()
    {
        ActiveLogState.Select(e => e.ActiveLogs);

        ActiveLogState.SelectedValueChanged += async (sender, activeLog) =>
        {
            if (activeLog == ImmutableDictionary<string, EventLogData>.Empty) { await SetActiveLog(null); }
        };

        base.OnInitialized();
    }

    private static string GetTabName(EventLogData log)
    {
        var firstEvent = log.Events.FirstOrDefault();

        return firstEvent is not null ?
            $"{firstEvent.LogName} - {firstEvent.ComputerName}" :
            Path.GetFileNameWithoutExtension(log.Name);
    }

    private static string GetTabTooltip(EventLogData log)
    {
        return $"{(log.Type == LogType.File ? "Log File: " : "Live Log: ")} {log.Name}\n" +
            $"Log Name: {log.Events.FirstOrDefault()?.LogName ?? ""}\n" +
            $"Computer Name: {log.Events.FirstOrDefault()?.ComputerName ?? ""}";
    }

    private string GetTabWidth()
    {
        var logCount = EventLogState.Value.ActiveLogs.Count;

        return logCount > 4 ? $"{100 / (logCount + 1)}vw" : "20vw";
    }

    private async Task SetActiveLog(string? activeLog)
    {
        ActiveLog = activeLog;
        await ActiveLogChanged.InvokeAsync(ActiveLog);
        Dispatcher.Dispatch(new EventLogAction.SelectLog(activeLog));
    }
}
