// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Globalization;

namespace EventLogExpert.Filtering.Emit;

/// <summary>
///     Compile-time helpers that turn raw lowered string values into emit-ready CLR-typed arrays. Done once at
///     compile time so the per-event hot path performs raw typed comparisons with no per-event coercion (per N-D4 / N-D6).
///     Values that cannot coerce to the target CLR type are dropped — they could never match at runtime through the
///     Dynamic.Core baseline either, since string equality between e.g. <c>"abc"</c> and <c>e.Id.ToString()</c> never
///     succeeds for any integer.
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
