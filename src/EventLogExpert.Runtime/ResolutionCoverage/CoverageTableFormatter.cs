// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Export;
using System.Globalization;
using System.Text;

namespace EventLogExpert.Runtime.ResolutionCoverage;

/// <summary>
///     Builds a TSV snapshot of a <see cref="ResolutionCoverageReport" /> for the clipboard. Bounded by
///     <see cref="MaxCopyRows" /> so a pathological high-cardinality log cannot force an unbounded contiguous-string
///     allocation; every cell is formula-neutralized and tab/newline-escaped so pasting into a spreadsheet is safe.
/// </summary>
public static class CoverageTableFormatter
{
    // Rows are distinct providers (far below this for any real log); the cap only bounds the single copied string.
    internal const int MaxCopyRows = 10_000;

    private const string RowSeparator = "\r\n";

    public static string Format(ResolutionCoverageReport report, bool isFiltered)
    {
        ArgumentNullException.ThrowIfNull(report);

        var builder = new StringBuilder();
        int rowCount = Math.Min(report.Rows.Count, MaxCopyRows);

        builder
            .Append("# ")
            .Append(isFiltered ? "All providers in the current view (filtered view)" : "All providers in the current view");

        if (report.Rows.Count > MaxCopyRows)
        {
            builder.Append(CultureInfo.InvariantCulture, $" - showing the first {rowCount:N0} of {report.Rows.Count:N0} providers");
        }

        builder.Append(RowSeparator);
        builder.Append("Provider\tEvents\tResolved\tNo provider\tNo message\tError\tCoverage").Append(RowSeparator);

        for (int i = 0; i < rowCount; i++)
        {
            var row = report.Rows[i];

            builder
                .Append(Cell(row.Provider)).Append('\t')
                .Append(row.Counts.Total.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(row.Counts.Resolved.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(row.Counts.NoProvider.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(row.Counts.NoMessage.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(row.Counts.Failed.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(CoverageStatusText.Label(row.Status)).Append(RowSeparator);
        }

        return builder.ToString();
    }

    // Formula-neutralize, then collapse tab/CR/LF to spaces so a value can never break the TSV grid.
    private static string Cell(string value) => TabularCellSanitizer.NeutralizeFormula(value)
        .Replace('\t', ' ')
        .Replace('\r', ' ')
        .Replace('\n', ' ');
}
