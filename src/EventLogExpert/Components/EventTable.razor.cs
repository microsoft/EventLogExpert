// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

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

    private string GetDescriptionStyle()
    {
        var total = _colWidths.Values.Sum() + (TableDividerWidth * _colWidths.Count) + ScrollBarWidth;
        return $"min-width: calc(100vw - {total}px); max-width: calc(100vw - {total}px);";
    }

    private string GetInlineStyle(string colName)
    {
        return $"min-width: {_colWidths[colName]}px; max-width: {_colWidths[colName]}px;";
    }
}
