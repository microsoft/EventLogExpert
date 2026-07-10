// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using System.Globalization;

namespace EventLogExpert.Runtime.LogTable;

/// <summary>Per-column grouping key mirroring the field each grouped comparer reads; "" is the no-value bucket.</summary>
internal static class ResolvedEventGroupKey
{
    /// <summary>
    ///     Reads the grouping bucket field for <paramref name="column" /> through the reader. DateAndTime uses invariant
    ///     Ticks (not the "O" string <c>AsString()</c> renders); every other column uses <c>AsString()</c>, which matches the
    ///     array-of-structs projection for each kind.
    /// </summary>
    public static string For(IEventColumnReader reader, EventLocator locator, ColumnName column)
    {
        if (column != ColumnName.DateAndTime)
        {
            return reader.GetField(locator, ColumnFieldMap.ToFieldId(column)).AsString();
        }

        return reader.GetField(locator, EventFieldId.TimeCreated).TryGetDateTime(out DateTime timeCreated)
            ? timeCreated.Ticks.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
    }
}
