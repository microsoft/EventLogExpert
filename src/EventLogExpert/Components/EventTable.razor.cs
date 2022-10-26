// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Diagnostics;

namespace EventLogExpert.Components;

public partial class EventTable
{
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

    private const int TableDividerWidth = 4;

    private const int ScrollBarWidth = 18;

    private string _columnResizingName = "";

    private double _columnMouseXLast = -1;

    private IJSObjectReference? _jsModule;

    private DotNetObjectReference<EventTable>? _thisObj;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        _jsModule = await JS.InvokeAsync<IJSObjectReference>("import", "./Components/EventTable.razor.js");
        _thisObj = DotNetObjectReference.Create(this);
    }

    private string GetDescriptionStyle()
    {
        var total = _colWidths.Values.Sum() + (TableDividerWidth * _colWidths.Count) + ScrollBarWidth;
        return $"min-width: calc(100vw - {total}px); max-width: calc(100vw - {total}px);";
    }

    private string GetInlineStyle(string colName)
    {
        return $"min-width: {_colWidths[colName]}px; max-width: {_colWidths[colName]}px;";
    }

    private void OnMouseDownDivider(MouseEventArgs e, string columnName)
    {
        _columnResizingName = columnName;
        _columnMouseXLast = e.ClientX;
        _jsModule?.InvokeVoidAsync("startColumnResize", new[] {_thisObj});
    }

    [JSInvokable]
    public void MouseMoveCallback(MouseEventArgs e)
    {
        int difference = (int)(_columnMouseXLast - e.ClientX);
        var newColumnWidth = _colWidths[_columnResizingName] - difference;
        if (newColumnWidth < 10) newColumnWidth = 10;

        _colWidths[_columnResizingName] = newColumnWidth;
        _columnMouseXLast = e.ClientX;
        StateHasChanged();
    }

    [JSInvokable]
    public void MouseUpCallback()
    {
        _columnResizingName = "";
        _columnMouseXLast = -1;
    }
}
