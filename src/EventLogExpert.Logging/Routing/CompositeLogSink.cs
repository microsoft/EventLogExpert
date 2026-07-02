// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Sinks;

namespace EventLogExpert.Logging.Routing;

public sealed class CompositeLogSink(IReadOnlyList<ILogSink> sinks, string category) : IProgress<LogRecord>
{
    private readonly IReadOnlyList<ILogSink> _sinks = sinks ?? throw new ArgumentNullException(nameof(sinks));

    public void Report(LogRecord value)
    {
        LogRecord routed = string.IsNullOrEmpty(value.Category) ? value with { Category = category } : value;

        foreach (ILogSink sink in _sinks)
        {
            try
            {
                sink.Emit(routed);
            }
            catch
            {
                // Best-effort: a sink fault (e.g. a transient file I/O error) must not break the operation or other sinks.
            }
        }
    }
}
