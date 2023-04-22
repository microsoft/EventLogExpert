// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventLogExpert.Components;

public partial class EventTable
{
    private const int ScrollBarWidth = 18;
    private const int TableDividerWidth = 4;

    private readonly Dictionary<string, int> _colWidths = new()
    {
        { "RecordId", 75 },
        { "TimeCreated", 165 },
        { "Id", 50 },
        { "MachineName", 100 },
        { "Level", 100 },
        { "ProviderName", 250 },
        { "Task", 150 }
    };

    private IJSObjectReference? _jsModule;
    private ElementReference _tableRef;

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _jsModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./Components/EventTable.razor.js");
            _jsModule?.InvokeVoidAsync("enableColumnResize", _tableRef);
        }

        await base.OnAfterRenderAsync(firstRender);
    }

    private string GetDescriptionStyle()
    {
        var total = _colWidths.Values.Sum() + (TableDividerWidth * _colWidths.Count) + ScrollBarWidth;
        return $"width: calc(100vw - {total}px);";
    }

    private string GetInlineStyle(string colName)
    {
        return $"width: {_colWidths[colName]}px;";
    }
}
