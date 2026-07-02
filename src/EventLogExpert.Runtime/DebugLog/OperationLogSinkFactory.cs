// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Routing;
using EventLogExpert.Logging.Sinks;

namespace EventLogExpert.Runtime.DebugLog;

internal sealed class OperationLogSinkFactory(FileLogSink fileSink, LogRoutingPolicy routingPolicy)
    : IOperationLogSinkFactory
{
    public IProgress<LogRecord> Create(IProgress<LogRecord> uiProgress, string category, bool verbose)
    {
        List<ILogSink> sinks = [new UiStreamingSink(uiProgress, routingPolicy.UiMinimumFor(verbose)), fileSink];

        return new CompositeLogSink(sinks, category);
    }
}
