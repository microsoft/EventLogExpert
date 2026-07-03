// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Sinks;

namespace EventLogExpert.Logging.Routing;

public sealed class LogSourceFactory : ILogSourceFactory
{
    public const string DefaultCategory = "App";

    private readonly ProcessOrigin _processOrigin;
    private readonly IReadOnlyList<ILogSink> _sinks;

    public LogSourceFactory(IEnumerable<ILogSink> sinks, ProcessOrigin processOrigin = ProcessOrigin.InProcess)
    {
        ArgumentNullException.ThrowIfNull(sinks);

        _sinks = [.. sinks];
        _processOrigin = processOrigin;
    }

    public ITraceLogger ForCategory(string category)
    {
        ArgumentException.ThrowIfNullOrEmpty(category);

        return new DispatchingTraceLogger(_sinks, category, _processOrigin);
    }
}
