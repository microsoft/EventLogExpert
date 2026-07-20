// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Readers;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Scenarios.Catalog;
using System.Collections.Frozen;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Scenarios;

internal sealed class ChannelPresenceProbe : IChannelPresenceProbe, IChannelReadinessService
{
    private static readonly IReadOnlySet<string> s_empty = FrozenSet<string>.Empty;

    private readonly IChannelConfigReader _configReader;
    private readonly ImmutableArray<string> _eagerConfigChannels;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<IEnumerable<string>> _readChannels;
    private readonly ITraceLogger _traceLogger;

    private Dictionary<string, ChannelConfigSnapshot> _config = new(StringComparer.OrdinalIgnoreCase);
    private FrozenSet<string>? _presentChannels;

    public ChannelPresenceProbe(
        ITraceLogger traceLogger,
        IChannelConfigReader configReader,
        BuiltInScenarioRegistry registry)
        : this(
            traceLogger,
            configReader,
            CatalogChannels(registry),
            static () => EventLogSession.GlobalSession.GetLogNames()) { }

    internal ChannelPresenceProbe(
        ITraceLogger traceLogger,
        IChannelConfigReader configReader,
        IEnumerable<string> eagerConfigChannels,
        Func<IEnumerable<string>> readChannels)
    {
        ArgumentNullException.ThrowIfNull(traceLogger);
        ArgumentNullException.ThrowIfNull(configReader);
        ArgumentNullException.ThrowIfNull(eagerConfigChannels);
        ArgumentNullException.ThrowIfNull(readChannels);

        _eagerConfigChannels = [.. eagerConfigChannels.Distinct(StringComparer.OrdinalIgnoreCase)];
        _configReader = configReader;
        _traceLogger = traceLogger;
        _readChannels = readChannels;
    }

    public IReadOnlySet<string> GetPresentChannels() => TryGetPresentChannels() ?? s_empty;

    public Task<ImmutableArray<ChannelReadiness>> GetReadinessAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GetReadinessCore(channels: null);
        }, cancellationToken);

    public Task<ImmutableArray<ChannelReadiness>> GetReadinessAsync(
        IEnumerable<string> channels,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channels);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GetReadinessCore(channels);
        }, cancellationToken);
    }

    public void Invalidate()
    {
        _gate.Wait();

        try
        {
            _presentChannels = null;
            _config = new Dictionary<string, ChannelConfigSnapshot>(StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool IsPresent(string logName) => GetPresentChannels().Contains(logName);

    public Task PrimeAsync() => Task.Run(GetPresentChannels);

    public IReadOnlySet<string>? TryGetPresentChannels()
    {
        var cached = _presentChannels;

        if (cached is not null) { return cached; }

        _gate.Wait();

        try
        {
            return EnsurePresentChannels();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static ImmutableArray<string> CatalogChannels(BuiltInScenarioRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return
        [
            .. registry.Scenarios
                .SelectMany(static scenario => scenario.Channels.Concat(scenario.OptionalChannels))
                .Distinct(StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static ChannelEnablement ToEnablement(bool? enabled) => enabled switch
    {
        true => ChannelEnablement.Enabled,
        false => ChannelEnablement.Disabled,
        _ => ChannelEnablement.Unknown
    };

    private void EnrichConfig(IEnumerable<string> channels)
    {
        foreach (var channel in channels)
        {
            if (string.IsNullOrWhiteSpace(channel) || _config.ContainsKey(channel)) { continue; }

            try
            {
                var config = _configReader.ReadConfig(channel);
                _config[channel] = new ChannelConfigSnapshot(ToEnablement(config.Enabled), config.Access);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException
                                                  and not StackOverflowException
                                                  and not AccessViolationException)
            {
                _traceLogger.Warning($"ChannelPresenceProbe: failed to read config for {channel}: {exception.Message}");
                _config[channel] = ChannelConfigSnapshot.Unknown;
            }
        }
    }

    private FrozenSet<string>? EnsurePresentChannels()
    {
        if (_presentChannels is not null) { return _presentChannels; }

        var loaded = TryReadChannels();

        if (loaded is not null)
        {
            _presentChannels = loaded;
        }

        return loaded;
    }

    private ImmutableArray<ChannelReadiness> GetReadinessCore(IEnumerable<string>? channels)
    {
        var requested = channels?
            .Where(channel => !string.IsNullOrWhiteSpace(channel))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

        _gate.Wait();

        try
        {
            var presentChannels = EnsurePresentChannels();
            ImmutableArray<string> targets = requested ?? [.. (presentChannels ?? s_empty).OrderBy(channel => channel, StringComparer.OrdinalIgnoreCase)];

            if (presentChannels is not null)
            {
                EnrichConfig(requested is not null ? targets : _eagerConfigChannels);
            }

            return
            [
                .. targets.Select(channel =>
                {
                    var presence = presentChannels is null
                        ? ChannelPresence.Unknown
                        : presentChannels.Contains(channel) ? ChannelPresence.Present : ChannelPresence.Absent;

                    var config = _config.GetValueOrDefault(channel, ChannelConfigSnapshot.Unknown);

                    return new ChannelReadiness(
                        channel,
                        presence,
                        config.Enablement)
                    {
                        Access = config.Access
                    };
                })
            ];
        }
        finally
        {
            _gate.Release();
        }
    }

    private FrozenSet<string>? TryReadChannels()
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

    private readonly record struct ChannelConfigSnapshot(ChannelEnablement Enablement, ChannelAccess Access)
    {
        internal static ChannelConfigSnapshot Unknown { get; } =
            new(ChannelEnablement.Unknown, ChannelAccess.Unknown);
    }
}
