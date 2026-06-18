// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Globalization;
using System.Runtime.CompilerServices;

namespace EventLogExpert.Runtime.Tests.Export;

internal sealed class CountingRowSource(int rowCount)
{
    public Action<int>? AfterRowProduced { get; set; }

    public int RowsProduced { get; private set; }

    public async IAsyncEnumerable<IReadOnlyList<string?>> GetRowsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < rowCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RowsProduced++;

            yield return [i.ToString(CultureInfo.InvariantCulture), $"value-{i}"];

            AfterRowProduced?.Invoke(i);

            await Task.Yield();
        }
    }
}
