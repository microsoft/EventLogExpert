// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.LogTable;

namespace EventLogExpert.Runtime.Tests.LogTable;

public sealed class CancellableSortTests
{
    [Fact]
    public void CancellableSort_CanceledMidSort_ObservesCancellationWithinCheckStride_OnSortedInput()
    {
        const int Count = 200_000;
        const int CancelAtComparison = 5_000;
        Comparison<int> inner = StrictOrderByValue([.. Enumerable.Range(0, Count)]);
        int[] keys = [.. Enumerable.Range(0, Count)];
        using var cancellation = new CancellationTokenSource();

        int comparisons = 0;
        Comparison<int> counting = (first, second) =>
        {
            if (++comparisons == CancelAtComparison) { cancellation.Cancel(); }

            return inner(first, second);
        };

        Assert.Throws<OperationCanceledException>(() =>
            ColumnDirectSort.CancellableSort(keys, counting, cancellation.Token));

        Assert.True(
            comparisons - CancelAtComparison <= 16_384,
            $"cancellation observed after {comparisons - CancelAtComparison} comparisons, exceeding the check stride");
    }

    [Fact]
    public void CancellableSort_CanceledMidSort_PermutationShape_Throws()
    {
        const int Count = 200_000;
        int[] keys = ShuffledIndices(Count, seed: 11);
        Comparison<int> inner = StrictOrderByValue(RandomValues(Count, seed: 11, modulo: 65_536));
        using var cancellation = new CancellationTokenSource();

        int comparisons = 0;
        Comparison<int> canceling = (first, second) =>
        {
            if (++comparisons == 5_000) { cancellation.Cancel(); }

            return inner(first, second);
        };

        Assert.Throws<OperationCanceledException>(() =>
            ColumnDirectSort.CancellableSort(keys, canceling, cancellation.Token));
    }

    [Fact]
    public void CancellableSort_CanceledMidSort_StringCompareShape_Throws()
    {
        const int Count = 200_000;
        var values = new string[Count];

        for (int index = 0; index < Count; index++)
        {
            values[index] = "row-" + (index * 2_654_435_761u % 50_000);
        }

        int[] order = ShuffledIndices(Count, seed: 5);
        using var cancellation = new CancellationTokenSource();

        int comparisons = 0;
        Comparison<int> canceling = (first, second) =>
        {
            if (++comparisons == 5_000) { cancellation.Cancel(); }

            return string.Compare(values[first], values[second], StringComparison.Ordinal);
        };

        Assert.Throws<OperationCanceledException>(() =>
            ColumnDirectSort.CancellableSort(order, canceling, cancellation.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(1_000)]
    [InlineData(100_000)]
    public void CancellableSort_MatchesArraySortPermutation_AcrossDistributions(int count)
    {
        foreach (int[] values in Distributions(count))
        {
            Comparison<int> comparison = StrictOrderByValue(values);
            int[] keys = ShuffledIndices(count, seed: 20260813);

            int[] expected = (int[])keys.Clone();
            Array.Sort(expected, comparison);

            int[] actual = (int[])keys.Clone();
            ColumnDirectSort.CancellableSort(actual, comparison, TestContext.Current.CancellationToken);

            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void CancellableSort_PreCanceledToken_Throws()
    {
        int[] keys = ShuffledIndices(1_000, seed: 3);
        Comparison<int> comparison = StrictOrderByValue(RandomValues(1_000, seed: 3, modulo: 256));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            ColumnDirectSort.CancellableSort(keys, comparison, cancellation.Token));
    }

    [Fact]
    public void HeapSortForTest_CanceledMidSort_Throws()
    {
        const int Count = 40_000;
        Comparison<int> inner = StrictOrderByValue(RandomValues(Count, seed: 21, modulo: 20_000));
        int[] keys = ShuffledIndices(Count, seed: 21);
        using var cancellation = new CancellationTokenSource();

        int comparisons = 0;
        Comparison<int> canceling = (first, second) =>
        {
            if (++comparisons == 5_000) { cancellation.Cancel(); }

            return inner(first, second);
        };

        Assert.Throws<OperationCanceledException>(() =>
            ColumnDirectSort.HeapSortForTest(keys, canceling, cancellation.Token));
    }

    [Fact]
    public void HeapSortForTest_MatchesArraySortPermutation_OnLargeInput()
    {
        const int Count = 20_000;
        int[] values = RandomValues(Count, seed: 99, modulo: 4_096);
        Comparison<int> comparison = StrictOrderByValue(values);
        int[] keys = ShuffledIndices(Count, seed: 7);

        int[] expected = (int[])keys.Clone();
        Array.Sort(expected, comparison);

        int[] actual = (int[])keys.Clone();
        ColumnDirectSort.HeapSortForTest(actual, comparison, TestContext.Current.CancellationToken);

        Assert.Equal(expected, actual);
    }

    private static IEnumerable<int[]> Distributions(int count)
    {
        yield return RandomValues(count, seed: 42, modulo: Math.Max(1, count));
        yield return [.. Enumerable.Range(0, count)];
        yield return [.. Enumerable.Range(0, count).Reverse()];
        yield return new int[count];
        yield return DuplicateHeavy(count);
        yield return OrganPipe(count);
    }

    private static int[] DuplicateHeavy(int count)
    {
        int[] values = new int[count];

        for (int index = 0; index < count; index++) { values[index] = index % 4; }

        return values;
    }

    private static int[] OrganPipe(int count)
    {
        int[] values = new int[count];

        for (int index = 0; index < count; index++) { values[index] = Math.Min(index, count - 1 - index); }

        return values;
    }

    private static int[] RandomValues(int count, int seed, int modulo)
    {
        var random = new Random(seed);
        int[] values = new int[count];

        for (int index = 0; index < count; index++) { values[index] = random.Next(modulo); }

        return values;
    }

    private static int[] ShuffledIndices(int count, int seed)
    {
        int[] indices = [.. Enumerable.Range(0, count)];
        var random = new Random(seed);

        for (int index = count - 1; index > 0; index--)
        {
            int swap = random.Next(index + 1);
            (indices[index], indices[swap]) = (indices[swap], indices[index]);
        }

        return indices;
    }

    private static Comparison<int> StrictOrderByValue(int[] values) => (first, second) =>
    {
        int byValue = values[first].CompareTo(values[second]);

        return byValue != 0 ? byValue : first.CompareTo(second);
    };
}
