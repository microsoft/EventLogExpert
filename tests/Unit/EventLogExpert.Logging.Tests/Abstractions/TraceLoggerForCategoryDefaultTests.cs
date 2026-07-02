// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Tests.Abstractions;

public sealed class TraceLoggerForCategoryDefaultTests
{
    [Fact]
    public void ForCategory_DefaultImplementation_ReturnsTheSameInstance_WhenNotOverridden()
    {
        // A logger that does not support categories inherits the default interface method, which is a no-op
        // (returns self). Categorizing loggers must override it - this locks the "override or lose it" contract.
        ITraceLogger logger = new NonCategorizingTraceLogger();

        Assert.Same(logger, logger.ForCategory(LogCategories.OfflineWim));
    }

    private sealed class NonCategorizingTraceLogger : ITraceLogger
    {
        public LogLevel MinimumLevel => LogLevel.None;

        public void Critical(CriticalLogHandler handler) => handler.ToStringAndClear();

        public void Debug(DebugLogHandler handler) => handler.ToStringAndClear();

        public void Error(ErrorLogHandler handler) => handler.ToStringAndClear();

        public void Information(InformationLogHandler handler) => handler.ToStringAndClear();

        public void Trace(TraceLogHandler handler) => handler.ToStringAndClear();

        public void Warning(WarningLogHandler handler) => handler.ToStringAndClear();
    }
}
