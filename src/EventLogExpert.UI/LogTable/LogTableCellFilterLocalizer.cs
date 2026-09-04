// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.LogTable;

internal static class LogTableCellFilterLocalizer
{
    public static string Describe(
        IStringLocalizer<SharedResource> localizer,
        bool exclude,
        bool isKeywords,
        bool hasValue,
        string columnLabel,
        string value) => (exclude, isKeywords, hasValue) switch
        {
            (false, false, true) => localizer["CellFilter_IncludeWhereEquals", columnLabel, value],
            (false, true, true) => localizer["CellFilter_IncludeWhereHas", columnLabel, value],
            (true, false, true) => localizer["CellFilter_ExcludeWhereEquals", columnLabel, value],
            (true, true, true) => localizer["CellFilter_ExcludeWhereHas", columnLabel, value],
            (false, _, false) => localizer["CellFilter_IncludeWhereNoValue", columnLabel],
            (true, _, false) => localizer["CellFilter_ExcludeWhereNoValue", columnLabel],
        };
}