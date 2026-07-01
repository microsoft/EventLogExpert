// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Configuration;
using EventLogExpert.Logging.Routing;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.DebugLog;
using EventLogExpert.Runtime.Settings;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EventLogExpert.Runtime.IntegrationTests.DebugLog;

public sealed class OperationLogSinkFactoryTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _testLogPath;

    public OperationLogSinkFactoryTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"OperationLogSinkFactoryTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _testLogPath = Path.Combine(_testDirectory, "debug.log");
    }

    [Fact]
    public void Create_OperationSink_StreamsEverythingToTheUi_ButThrottlesTheFileToWarning()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.LogLevel.Returns(LogLevel.Information);
        var policy = new LogRoutingPolicy(LoggingOptions.CreateShippedDefaults(), settings.LogLevel);
        using var fileSink = new DebugFileSink(new FileLocationOptions(_testDirectory), settings, policy);
        var factory = new OperationLogSinkFactory(fileSink, policy);
        var uiCaptured = new List<LogRecord>();

        IProgress<LogRecord> logSink = factory.Create(
            new CapturingProgress(uiCaptured), LogCategories.DatabaseTools, verbose: false);

        logSink.Report(new LogRecord(DateTime.UtcNow, LogLevel.Information, "progress"));
        logSink.Report(new LogRecord(DateTime.UtcNow, LogLevel.Warning, "problem"));

        string[] uiMessages = [.. uiCaptured.Select(record => record.Message)];
        Assert.Equal(["progress", "problem"], uiMessages);

        string fileContent = ReadLogFile();
        Assert.DoesNotContain("progress", fileContent);
        Assert.Contains("problem", fileContent);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
        catch
        {
            // Ignore cleanup errors in tests
        }
    }

    private string ReadLogFile()
    {
        using var stream = new FileStream(
            _testLogPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private sealed class CapturingProgress(List<LogRecord> captured) : IProgress<LogRecord>
    {
        public void Report(LogRecord value) => captured.Add(value);
    }
}
