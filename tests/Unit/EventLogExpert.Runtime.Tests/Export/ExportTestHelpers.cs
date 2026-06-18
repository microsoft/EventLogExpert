// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Globalization;
using System.Text;

namespace EventLogExpert.Runtime.Tests.Export;

internal static class ExportTestHelpers
{
    public static string DecodeWithoutBom(byte[] bytes)
    {
        ReadOnlySpan<byte> span = bytes;
        ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];

        if (span.StartsWith(bom))
        {
            span = span[bom.Length..];
        }

        return Encoding.UTF8.GetString(span);
    }

    public static IEnumerable<IReadOnlyList<string?>> GenerateRows(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return [i.ToString(CultureInfo.InvariantCulture), $"value-{i}"];
        }
    }

    public static bool StartsWithUtf8Bom(byte[] bytes) =>
        bytes is [0xEF, 0xBB, 0xBF, ..];

    public static async IAsyncEnumerable<IReadOnlyList<string?>> ToAsync(IEnumerable<IReadOnlyList<string?>> rows)
    {
        foreach (IReadOnlyList<string?> row in rows)
        {
            yield return row;
            await Task.Yield();
        }
    }
}
