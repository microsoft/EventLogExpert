// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Globalization;

namespace EventLogExpert.Filtering.Emit;

/// <summary>
///     Coerces lowered string values into typed CLR arrays once at compile time, so per-event predicates skip
///     re-coercion. Unparseable values are dropped — they could never match against a typed field at runtime.
/// </summary>
internal static class CompileTimeLiterals
{
    public static Guid[] CoerceToGuidArray(IReadOnlyList<string> values)
    {
        var coerced = new List<Guid>(values.Count);

        foreach (var value in values)
        {
            if (Guid.TryParse(value.AsSpan().Trim(), out var parsed))
            {
                coerced.Add(parsed);
            }
        }

        return coerced.ToArray();
    }

    public static int[] CoerceToIntArray(IReadOnlyList<string> values)
    {
        var coerced = new List<int>(values.Count);

        foreach (var value in values)
        {
            if (int.TryParse(value.AsSpan().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                coerced.Add(parsed);
            }
        }

        return coerced.ToArray();
    }

    public static long[] CoerceToLongArray(IReadOnlyList<string> values)
    {
        var coerced = new List<long>(values.Count);

        foreach (var value in values)
        {
            if (long.TryParse(value.AsSpan().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                coerced.Add(parsed);
            }
        }

        return coerced.ToArray();
    }

    public static string[] Snapshot(IReadOnlyList<string> values)
    {
        var snapshot = new string[values.Count];

        for (var i = 0; i < values.Count; i++)
        {
            snapshot[i] = values[i];
        }

        return snapshot;
    }
}
