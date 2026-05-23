// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.EventDbTool.Tests;

public sealed class ProgramTests
{
    [Fact]
    public void BuildServiceProvider_RegistersITraceLoggerAsSingleton()
    {
        // Arrange / Act — the same provider should hand out the same logger instance for repeated
        // resolves, which is the behavior the rest of the tool depends on (a per-call logger would
        // double-buffer trace output and lose verbose-level config).
        using var sp = Program.BuildServiceProvider(false);

        var first = sp.GetRequiredService<ITraceLogger>();
        var second = sp.GetRequiredService<ITraceLogger>();

        // Assert
        Assert.Same(first, second);
    }

    [Fact]
    public void BuildServiceProvider_WhenVerboseFalse_RegistersLoggerAtInformationLevel()
    {
        // Default CLI verbosity is Information so users see progress but not internal trace noise.
        using var sp = Program.BuildServiceProvider(false);

        var logger = sp.GetRequiredService<ITraceLogger>();

        Assert.Equal(LogLevel.Information, logger.MinimumLevel);
    }

    [Fact]
    public void BuildServiceProvider_WhenVerboseTrue_RegistersLoggerAtTraceLevel()
    {
        // The --verbose flag is the only way to surface Trace/Debug logs, so the contract that
        // verbose=true ⇒ MinimumLevel=Trace is load-bearing for troubleshooting workflows.
        using var sp = Program.BuildServiceProvider(true);

        var logger = sp.GetRequiredService<ITraceLogger>();

        Assert.Equal(LogLevel.Trace, logger.MinimumLevel);
    }
}
