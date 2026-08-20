// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Buffers;

namespace EventLogExpert.Runtime.Export;

// Neutralizes spreadsheet formula injection for tabular exports (CSV, TSV): a cell whose first non-whitespace character
// is a formula trigger (= + - @) or a leading tab/CR is prefixed with an apostrophe so a spreadsheet treats it as text.
// Shared by the event-table CSV exporter and the coverage-table TSV clipboard copy.
internal static class TabularCellSanitizer
{
    private static readonly SearchValues<char> s_formulaInjectionTriggers = SearchValues.Create("=+-@");

    public static string NeutralizeFormula(string value)
    {
        if (value.Length == 0) { return value; }

        if (value[0] is '\t' or '\r') { return "'" + value; }

        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character)) { continue; }

            return s_formulaInjectionTriggers.Contains(character) ? "'" + value : value;
        }

        return value;
    }
}
