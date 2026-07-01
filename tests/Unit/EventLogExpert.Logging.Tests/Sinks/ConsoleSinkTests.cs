// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Sinks;
using Microsoft.Extensions.Logging;
using System.Text;

namespace EventLogExpert.Logging.Tests.Sinks;

public sealed class ConsoleSinkTests
{
    [Fact]
    public void Emit_BelowMinimumLevel_WritesNothing()
    {
        var sink = new ConsoleSink(LogLevel.Warning);

        string output = CaptureConsoleOut(() => sink.Emit(new LogRecord(DateTime.UtcNow, LogLevel.Information, "info")));

        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public void Emit_InformationLevel_WritesBareMessage_WithoutLevelPrefix()
    {
        var sink = new ConsoleSink(LogLevel.Trace);

        string output = CaptureConsoleOut(() => sink.Emit(new LogRecord(DateTime.UtcNow, LogLevel.Information, "hello")));

        Assert.Equal($"hello{Environment.NewLine}", output);
    }

    [Fact]
    public void Emit_NonInformationLevel_PrefixesTheLevelName()
    {
        var sink = new ConsoleSink(LogLevel.Trace);

        string output = CaptureConsoleOut(() => sink.Emit(new LogRecord(DateTime.UtcNow, LogLevel.Warning, "careful")));

        Assert.Equal($"[Warning] careful{Environment.NewLine}", output);
    }

    [Fact]
    public void Emit_WhenConsoleWriteThrowsIOException_DoesNotPropagate()
    {
        var originalOut = Console.Out;

        try
        {
            Console.SetOut(new ThrowingTextWriter());
            var sink = new ConsoleSink(LogLevel.Trace);

            Exception? exception = Record.Exception(() =>
                sink.Emit(new LogRecord(DateTime.UtcNow, LogLevel.Error, "boom")));

            Assert.Null(exception);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private static string CaptureConsoleOut(Action action)
    {
        var originalOut = Console.Out;
        var buffer = new StringWriter();

        try
        {
            Console.SetOut(buffer);
            action();
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        return buffer.ToString();
    }

    private sealed class ThrowingTextWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => throw new IOException("stdout is detached");

        public override void Write(string? value) => throw new IOException("stdout is detached");

        public override void WriteLine(string? value) => throw new IOException("stdout is detached");
    }
}
