// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Runtime.LogTable.OrderedView;

internal interface IReaderResolver
{
    int Count { get; }

    IEventColumnReader Resolve(in EventLocator locator);

    bool TryResolve(in EventLocator locator, [NotNullWhen(true)] out IEventColumnReader? reader);

    bool TryResolveByLog(EventLogId logId, int generation, [NotNullWhen(true)] out IEventColumnReader? reader);
}

internal sealed class FrozenReaderResolver : IReaderResolver
{
    private readonly Dictionary<LogGeneration, IEventColumnReader> _readers;

    internal FrozenReaderResolver(Dictionary<LogGeneration, IEventColumnReader> readers) => _readers = readers;

    public int Count => _readers.Count;

    public IEventColumnReader Resolve(in EventLocator locator) =>
        TryResolve(locator, out IEventColumnReader? reader)
            ? reader
            : throw new KeyNotFoundException($"{nameof(FrozenReaderResolver)}: no frozen reader for the requested log generation.");

    public bool TryResolve(in EventLocator locator, [NotNullWhen(true)] out IEventColumnReader? reader) =>
        _readers.TryGetValue(new LogGeneration(locator.LogId, locator.Generation), out reader);

    public bool TryResolveByLog(EventLogId logId, int generation, [NotNullWhen(true)] out IEventColumnReader? reader) =>
        _readers.TryGetValue(new LogGeneration(logId, generation), out reader);
}

internal sealed class LiveReaderResolver : IReaderResolver
{
    private readonly Dictionary<LogGeneration, IEventColumnReader> _latestReaders;

    internal LiveReaderResolver(Dictionary<LogGeneration, IEventColumnReader> latestReaders) => _latestReaders = latestReaders;

    public int Count => _latestReaders.Count;

    public IEventColumnReader Resolve(in EventLocator locator) =>
        TryResolve(locator, out IEventColumnReader? reader)
            ? reader
            : throw new KeyNotFoundException($"{nameof(LiveReaderResolver)}: no live reader for the requested log generation.");

    public bool TryResolve(in EventLocator locator, [NotNullWhen(true)] out IEventColumnReader? reader) =>
        _latestReaders.TryGetValue(new LogGeneration(locator.LogId, locator.Generation), out reader);

    public bool TryResolveByLog(EventLogId logId, int generation, [NotNullWhen(true)] out IEventColumnReader? reader) =>
        _latestReaders.TryGetValue(new LogGeneration(logId, generation), out reader);
}
