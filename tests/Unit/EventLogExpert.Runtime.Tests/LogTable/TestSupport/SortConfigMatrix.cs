// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable;

namespace EventLogExpert.Runtime.Tests.LogTable.TestSupport;

internal readonly record struct SortConfig(ColumnName? OrderBy, bool IsDescending, ColumnName? GroupBy, bool IsGroupDescending);

internal static class SortConfigMatrix
{
    private static readonly ColumnName[] s_allColumns = Enum.GetValues<ColumnName>();
    private static readonly bool[] s_bools = [false, true];

    internal static IReadOnlyList<SortConfig> All()
    {
        var configs = new List<SortConfig>();

        foreach (ColumnName? orderBy in OrderByOptions())
        {
            foreach (bool isDescending in s_bools)
            {
                configs.Add(new SortConfig(orderBy, isDescending, null, false));
            }
        }

        foreach (ColumnName groupBy in s_allColumns)
        {
            foreach (bool isGroupDescending in s_bools)
            {
                foreach (ColumnName? orderBy in OrderByOptions())
                {
                    foreach (bool isDescending in s_bools)
                    {
                        configs.Add(new SortConfig(orderBy, isDescending, groupBy, isGroupDescending));
                    }
                }
            }
        }

        return configs;
    }

    private static IEnumerable<ColumnName?> OrderByOptions()
    {
        yield return null;

        foreach (ColumnName column in s_allColumns)
        {
            yield return column;
        }
    }
}
