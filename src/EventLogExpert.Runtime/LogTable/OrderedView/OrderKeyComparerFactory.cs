// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.LogTable.OrderedView;

internal static class OrderKeyComparerFactory
{
    internal static IComparer<OrderKey> Empty { get; } = new EmptyOrderKeyComparer();

    internal static IComparer<OrderKey> Create(SortContext context, IReaderResolver resolver)
    {
        ResolvedEventOrdering.CrossComparison cross = ResolvedEventOrdering.SelectCrossColumnComparer(
            context.OrderBy, context.IsDescending, context.GroupBy, context.IsGroupDescending);

        return new DelegatingOrderKeyComparer(cross, resolver);
    }

    private sealed class EmptyOrderKeyComparer : IComparer<OrderKey>
    {
        public int Compare(OrderKey x, OrderKey y) => 0;
    }
}

internal sealed class DelegatingOrderKeyComparer : IComparer<OrderKey>
{
    private readonly ResolvedEventOrdering.CrossComparison _cross;
    private readonly IReaderResolver _resolver;

    internal DelegatingOrderKeyComparer(ResolvedEventOrdering.CrossComparison cross, IReaderResolver resolver)
    {
        _cross = cross;
        _resolver = resolver;
    }

    internal int PinnedReaderCount => _resolver.Count;

    internal IReaderResolver Resolver => _resolver;

    public int Compare(OrderKey x, OrderKey y)
    {
        EventLocator left = x.Locator;
        EventLocator right = y.Locator;

        IEventColumnReader readerLeft = _resolver.Resolve(left);
        IEventColumnReader readerRight = _resolver.Resolve(right);

        int byColumn = _cross(readerLeft, left, readerRight, right);

        return byColumn != 0 ? byColumn : CompareIdentity(left, right);
    }

    private static int CompareIdentity(in EventLocator left, in EventLocator right)
    {
        int byLog = left.LogId.Value.CompareTo(right.LogId.Value);

        if (byLog != 0) { return byLog; }

        if (left.Generation != right.Generation) { return left.Generation < right.Generation ? -1 : 1; }

        return left.Index.CompareTo(right.Index);
    }
}
