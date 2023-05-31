// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Components;

public partial class SplitLogTabPane
{
    [Parameter] public string? ActiveLog { get; set; }

    [Parameter] public EventCallback<string?> ActiveLogChanged { get; set; }

    private static string GetTabName(string path) => Path.GetFileNameWithoutExtension(path);

    private async Task SetActiveLog(string? activeLog)
    {
        ActiveLog = activeLog;
        await ActiveLogChanged.InvokeAsync(ActiveLog);
    }
}
