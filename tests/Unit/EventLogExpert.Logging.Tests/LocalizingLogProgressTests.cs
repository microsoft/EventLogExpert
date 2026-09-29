// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Logging.Tests;

public sealed class LocalizingLogProgressTests
{
    [Fact]
    public void DefaultInterfaceMethod_DelegatesToTwoArgumentOverload()
    {
        INeutralTextResolver resolver = new TestNeutralTextResolver();

        Assert.Equal("k(a)", resolver.Resolve("k", ["a"], pluralCount: 9));
    }

    [Fact]
    public void Report_KeyedEmptyMessage_FillsNeutralMessage()
    {
        var captured = new List<LogRecord>();
        var progress = new LocalizingLogProgress(new TestNeutralTextResolver(), new CapturingProgress(captured));

        progress.Report(new LogRecord(
            DateTime.UtcNow,
            LogLevel.Information,
            string.Empty,
            MessageKey: "DatabaseTools_Op_Test",
            MessageArgs: ["alpha"]));

        Assert.Equal("DatabaseTools_Op_Test(alpha)", Assert.Single(captured).Message);
    }

    [Fact]
    public void Report_KeyedMessageWithPluralCount_PassesCountToResolver()
    {
        var captured = new List<LogRecord>();
        var recording = new RecordingNeutralTextResolver();
        var progress = new LocalizingLogProgress(recording, new CapturingProgress(captured));

        progress.Report(new LogRecord(
            DateTime.UtcNow,
            LogLevel.Information,
            string.Empty,
            MessageKey: "DatabaseTools_Op_Test",
            MessageArgs: ["alpha"],
            MessagePluralCount: 5));

        Assert.Equal(5L, recording.LastPluralCount);
        Assert.Equal("resolved-5", Assert.Single(captured).Message);
    }

    [Fact]
    public void Report_UnkeyedMessage_ForwardsOriginalMessage()
    {
        var captured = new List<LogRecord>();
        var progress = new LocalizingLogProgress(new TestNeutralTextResolver(), new CapturingProgress(captured));

        progress.Report(new LogRecord(DateTime.UtcNow, LogLevel.Information, "data row"));

        Assert.Equal("data row", Assert.Single(captured).Message);
    }

    private sealed class CapturingProgress(List<LogRecord> captured) : IProgress<LogRecord>
    {
        public void Report(LogRecord value) => captured.Add(value);
    }

    private sealed class RecordingNeutralTextResolver : INeutralTextResolver
    {
        public long? LastPluralCount { get; private set; }

        public string Resolve(string key, IReadOnlyList<string> args) => key;

        public string Resolve(string key, IReadOnlyList<string> args, long? pluralCount)
        {
            LastPluralCount = pluralCount;

            return $"resolved-{pluralCount}";
        }
    }

    private sealed class TestNeutralTextResolver : INeutralTextResolver
    {
        public string Resolve(string key, IReadOnlyList<string> args) => $"{key}({string.Join(",", args)})";
    }
}
