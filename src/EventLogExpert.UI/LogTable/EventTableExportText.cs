// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Export;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.UI.Common;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.LogTable;

internal sealed class EventTableExportText(IStringLocalizer<SharedResource> localizer) : IEventTableExportText
{
    public string DescriptionHeader => localizer["Copy_Markdown_DescriptionHeader"];

    public string ColumnHeader(ColumnName column, TimeZoneInfo timeZone)
    {
        if (column != ColumnName.DateAndTime)
        {
            return ColumnNameLocalizer.Label(localizer, column);
        }

        return timeZone.Equals(TimeZoneInfo.Local) ?
            localizer["Table_ColumnHeader_DateAndTime"].Value :
            localizer["Table_ColumnHeader_DateAndTimeWithZone", timeZone.DisplayName.Split(' ').First()].Value;
    }
}
