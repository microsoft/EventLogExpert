// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Evaluation;
using System.Collections.Immutable;
using System.Diagnostics;

namespace EventLogExpert.Runtime.LogTable.OrderedView;

internal readonly record struct ViewContentTokenReaderStamp(EventLogId LogId, int Generation, long ContentVersion, int Count);

public readonly struct ViewContentToken : IEquatable<ViewContentToken>
{
    private readonly Filter _filter;
    private readonly ImmutableArray<ViewContentTokenReaderStamp> _readers;
    private readonly int _survivorCount;

    private ViewContentToken(Filter filter, ImmutableArray<ViewContentTokenReaderStamp> readers, int survivorCount)
    {
        _filter = filter;
        _readers = readers;
        _survivorCount = survivorCount;
    }

    public static ViewContentToken Empty { get; } = new(default, ImmutableArray<ViewContentTokenReaderStamp>.Empty, 0);

    internal static ViewContentToken From(
        Filter filter,
        ImmutableHashSet<LogGeneration> inScope,
        OrderedViewSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (inScope.IsEmpty) { return Empty; }

        ImmutableArray<ViewContentTokenReaderStamp>.Builder builder =
            ImmutableArray.CreateBuilder<ViewContentTokenReaderStamp>(inScope.Count);

        foreach (LogGeneration member in inScope)
        {
            bool resolved = snapshot.TryGetReaderByLog(member.LogId, member.Generation, out IEventColumnReader? reader);

            Debug.Assert(resolved, "An in-scope member did not resolve a reader; the view constructor should have thrown.");

            if (resolved)
            {
                builder.Add(
                    new ViewContentTokenReaderStamp(member.LogId, member.Generation, reader!.ContentVersion, reader.Count));
            }
        }

        return FromStamps(filter, builder.ToImmutable(), snapshot.Count);
    }

    internal static ViewContentToken FromStamps(
        Filter filter, ImmutableArray<ViewContentTokenReaderStamp> readers, int survivorCount)
    {
        if (readers.IsDefaultOrEmpty) { return Empty; }

        ImmutableArray<ViewContentTokenReaderStamp> sorted = readers.Sort(static (left, right) =>
        {
            int byLog = left.LogId.Value.CompareTo(right.LogId.Value);

            return byLog != 0 ? byLog : left.Generation.CompareTo(right.Generation);
        });

        return new ViewContentToken(filter, sorted, survivorCount);
    }

    public static bool operator ==(ViewContentToken left, ViewContentToken right) => left.Equals(right);

    public static bool operator !=(ViewContentToken left, ViewContentToken right) => !left.Equals(right);

    public bool Equals(ViewContentToken other)
    {
        ImmutableArray<ViewContentTokenReaderStamp> readers =
            _readers.IsDefault ? ImmutableArray<ViewContentTokenReaderStamp>.Empty : _readers;
        ImmutableArray<ViewContentTokenReaderStamp> otherReaders =
            other._readers.IsDefault ? ImmutableArray<ViewContentTokenReaderStamp>.Empty : other._readers;

        if (readers.Length != otherReaders.Length || _survivorCount != other._survivorCount) { return false; }

        if (readers.Length > 0 && !_filter.Equals(other._filter)) { return false; }

        for (int index = 0; index < readers.Length; index++)
        {
            if (!readers[index].Equals(otherReaders[index])) { return false; }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is ViewContentToken other && Equals(other);

    public override int GetHashCode()
    {
        ImmutableArray<ViewContentTokenReaderStamp> readers =
            _readers.IsDefault ? ImmutableArray<ViewContentTokenReaderStamp>.Empty : _readers;

        var hash = new HashCode();
        hash.Add(_survivorCount);

        if (readers.Length > 0) { hash.Add(_filter); }

        foreach (ViewContentTokenReaderStamp reader in readers) { hash.Add(reader); }

        return hash.ToHashCode();
    }
}
