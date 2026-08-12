// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable.OrderedView;

internal sealed class RowCoverage
{
    private readonly Dictionary<LogGeneration, int> _byKey;

    internal RowCoverage(Dictionary<LogGeneration, int> byKey) => _byKey = byKey;

    public IEnumerable<KeyValuePair<LogGeneration, int>> Entries => _byKey;

    public int RowCount
    {
        get
        {
            int total = 0;

            foreach (int covered in _byKey.Values) { total += covered; }

            return total;
        }
    }

    public int CoverageOf(in LogGeneration key) => _byKey.GetValueOrDefault(key);
}
