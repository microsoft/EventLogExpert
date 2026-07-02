// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.DebugLog;
using EventLogExpert.Runtime.IntegrationTests.TestUtils.Constants;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Runtime.IntegrationTests.DebugLog;

public sealed class DebugLogFormatterTests
{
    [Fact]
    public void Format_ShouldIncludeThreadId()
    {
        int expectedThreadId = Environment.CurrentManagedThreadId;

        string formatted = DebugLogFormatter.Format(new LogRecord(
            DateTime.UtcNow,
            LogLevel.Information,
            Constants.DebugLogTestMessage));

        Assert.Contains($"[{expectedThreadId}]", formatted);
    }

    [Fact]
    public void Format_ShouldIncludeTimestamp()
    {
        string formatted = DebugLogFormatter.Format(new LogRecord(
            DateTime.UtcNow,
            LogLevel.Information,
            Constants.DebugLogTestMessage));

        Assert.Matches(@"\[\d{4}-\d{2}-\d{2}", formatted);
    }

    [Theory]
    [InlineData(ProcessOrigin.ElevatedHelper, true)]
    [InlineData(ProcessOrigin.InProcess, false)]
    public void Format_ShouldPrefixElevatedHelperTag_OnlyForElevatedHelperOrigin(ProcessOrigin origin, bool expectTag)
    {
        string formatted = DebugLogFormatter.Format(new LogRecord(
            DateTime.UtcNow,
            LogLevel.Information,
            Constants.DebugLogTestMessage,
            string.Empty,
            origin));

        if (expectTag)
        {
            Assert.Contains($"[ElevatedHelper] {Constants.DebugLogTestMessage}", formatted);
        }
        else
        {
            Assert.Contains(Constants.DebugLogTestMessage, formatted);
            Assert.DoesNotContain("[ElevatedHelper]", formatted);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(LogCategories.DatabaseToolsCreate)]
    [InlineData("Offline.Wim")]
    public void Format_WhenParsed_ShouldRoundTripElevatedHelperCategoryLevelAndMessage(string category)
    {
        AssertRoundTrip(category, ProcessOrigin.ElevatedHelper, LogLevel.Debug);
        AssertRoundTrip(category, ProcessOrigin.ElevatedHelper, LogLevel.Critical);
    }

    [Theory]
    [InlineData("")]
    [InlineData(LogCategories.DatabaseToolsCreate)]
    [InlineData("Offline.Wim")]
    public void Format_WhenParsed_ShouldRoundTripInProcessCategoryLevelAndMessage(string category)
    {
        AssertRoundTrip(category, ProcessOrigin.InProcess, LogLevel.Information);
        AssertRoundTrip(category, ProcessOrigin.InProcess, LogLevel.Warning);
    }

    [Fact]
    public void Format_WhenRecordHasCategory_ShouldRenderCategorySegment()
    {
        string formatted = DebugLogFormatter.Format(new LogRecord(
            DateTime.UtcNow,
            LogLevel.Warning,
            Constants.DebugLogTestMessage,
            LogCategories.DatabaseToolsCreate));

        Assert.Contains($"[{LogCategories.DatabaseToolsCreate}] {Constants.DebugLogTestMessage}", formatted);
    }

    [Fact]
    public void Format_WhenRecordHasCategoryAndElevatedHelperOrigin_ShouldRenderBothSegments()
    {
        string formatted = DebugLogFormatter.Format(new LogRecord(
            DateTime.UtcNow,
            LogLevel.Warning,
            Constants.DebugLogTestMessage,
            LogCategories.DatabaseToolsCreate,
            ProcessOrigin.ElevatedHelper));

        Assert.Contains($"[{LogCategories.DatabaseToolsCreate}] [ElevatedHelper] {Constants.DebugLogTestMessage}", formatted);
    }

    [Fact]
    public void Format_WhenRecordHasEmptyOrigin_ShouldNotRenderCategorySegment()
    {
        string formatted = DebugLogFormatter.Format(new LogRecord(
            DateTime.UtcNow,
            LogLevel.Warning,
            Constants.DebugLogTestMessage,
            string.Empty));

        Assert.Contains($"[{nameof(LogLevel.Warning)}] {Constants.DebugLogTestMessage}", formatted);
        Assert.DoesNotContain("[DatabaseTools", formatted);
    }

    private static void AssertRoundTrip(string category, ProcessOrigin processOrigin, LogLevel level)
    {
        const string Message = "round trip message";
        LogRecord record = new(DateTime.UtcNow, level, Message, category, processOrigin);

        string formatted = DebugLogFormatter.Format(record);

        Assert.True(DebugLogEntryParser.TryParseLine(formatted, out DebugLogEntry? entry), formatted);
        Assert.Equal(string.IsNullOrEmpty(category) ? null : category, entry.Category);
        Assert.Equal(processOrigin, entry.ProcessOrigin);
        Assert.Equal(level, entry.Level);
        Assert.Equal(Message, entry.Message);
    }
}
