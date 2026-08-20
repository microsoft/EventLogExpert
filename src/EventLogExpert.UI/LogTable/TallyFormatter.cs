// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Globalization;

namespace EventLogExpert.UI.LogTable;

// Shared count/percentage formatting for the stats and coverage contributor tables so the "N0" and share math
// (including the zero-total guard and culture) lives in exactly one place.
internal static class TallyFormatter
{
    public static string Count(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    public static string Share(int count, int total) =>
        total == 0 ? "0%" : (count * 100.0 / total).ToString("0.0", CultureInfo.CurrentCulture) + "%";
}
