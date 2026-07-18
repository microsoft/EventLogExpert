// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Readers;
using EventLogExpert.Logging.Abstractions;
using System.Collections.Frozen;

namespace EventLogExpert.Runtime.Scenarios;

internal sealed class ChannelPresenceProbe : IChannelPresenceProbe
{
    private static readonly IReadOnlySet<string> s_empty = FrozenSet<string>.Empty;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<IEnumerable<string>> _readChannels;
    private readonly ITraceLogger _traceLogger;

    private IReadOnlySet<string>? _cache;

    public ChannelPresenceProbe(ITraceLogger traceLogger)
        : this(traceLogger, static () => EventLogSession.GlobalSession.GetLogNames()) { }

    internal ChannelPresenceProbe(ITraceLogger traceLogger, Func<IEnumerable<string>> readChannels)
    {
        ArgumentNullException.ThrowIfNull(traceLogger);
        ArgumentNullException.ThrowIfNull(readChannels);

        _traceLogger = traceLogger;
        _readChannels = readChannels;
    }

    public IReadOnlySet<string> GetPresentChannels() => TryGetPresentChannels() ?? s_empty;

    public bool IsPresent(string logName) => GetPresentChannels().Contains(logName);

    public Task PrimeAsync() => Task.Run(GetPresentChannels);

    public IReadOnlySet<string>? TryGetPresentChannels()
    {
        var cached = _cache;

        if (cached is not null) { return cached; }

        _gate.Wait();

        try
        {
            if (_cache is not null) { return _cache; }

            var loaded = TryReadChannels();

            // A failed read is not cached, so the next read retries.
            if (loaded is not null) { _cache = loaded; }

            return loaded;
        }
        finally
        {
            _gate.Release();
        }
    }

    private IReadOnlySet<string>? TryReadChannels()
    {
        try
        {
            return _readChannels().ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception)
        {
            _traceLogger.Warning($"ChannelPresenceProbe: failed to read channel names: {exception.Message}");

            return null;
        }
    }
}
