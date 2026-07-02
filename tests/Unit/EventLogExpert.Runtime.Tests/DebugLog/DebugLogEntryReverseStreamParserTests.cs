// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.DebugLog;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Runtime.Tests.DebugLog;

public sealed class DebugLogEntryReverseStreamParserTests
{
    [Fact]
    public void AddLine_WhenHeaderHasContinuations_ShouldFoldThemBackIntoFileOrder()
    {
        var parser = new DebugLogEntryReverseStreamParser();
        var header = Header("outer");

        // Newest-first arrival: an entry's continuation lines come BEFORE its header.
        Assert.Null(parser.AddLine("  at Frame2"));
        Assert.Null(parser.AddLine("  at Frame1"));
        var entry = parser.AddLine(header);

        Assert.NotNull(entry);
        Assert.Equal($"{header}\n  at Frame1\n  at Frame2", entry.RawLine);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Null(parser.Flush());
    }

    [Fact]
    public void AddLine_WhenHeaderOnly_ShouldNotAppendATrailingEmptyLine()
    {
        var parser = new DebugLogEntryReverseStreamParser();
        var header = Header("solo");

        var entry = parser.AddLine(header);

        Assert.NotNull(entry);
        Assert.Equal(header, entry.RawLine);
        Assert.Equal("solo", entry.Message);
    }

    [Fact]
    public void AddLine_WhenHeadersHaveNonChronologicalTimestamps_ShouldStillSplitEachIntoAnEntry()
    {
        var parser = new DebugLogEntryReverseStreamParser();
        var newer = HeaderAt("2024-06-01T12:00:00.0000000+00:00", "newer");
        var older = HeaderAt("2020-01-01T00:00:00.0000000+00:00", "older");

        var first = parser.AddLine(newer);
        var second = parser.AddLine(older);

        Assert.NotNull(first);
        Assert.Equal("newer", first.Message);
        Assert.NotNull(second);
        Assert.Equal("older", second.Message);
    }

    [Fact]
    public void Flush_WhenLeadingOrphanContinuations_ShouldEmitOneNullMetadataStandaloneEntry()
    {
        var parser = new DebugLogEntryReverseStreamParser();

        // Orphan continuation lines (before the file's first header) arrive newest-first.
        Assert.Null(parser.AddLine("orphan-b"));
        Assert.Null(parser.AddLine("orphan-a"));
        var entry = parser.Flush();

        Assert.NotNull(entry);
        Assert.Null(entry.Level);
        Assert.Null(entry.Timestamp);
        Assert.Null(entry.ThreadId);
        Assert.Equal("orphan-a\norphan-b", entry.RawLine);
    }

    [Fact]
    public void Flush_WhenNothingBuffered_ShouldReturnNull()
    {
        var parser = new DebugLogEntryReverseStreamParser();

        Assert.Null(parser.Flush());
    }

    [Fact]
    public void ReverseParse_ShouldEqualForwardParseReversed()
    {
        // A file that exercises every path: a leading orphan, a header-only entry, and a multi-line entry.
        var fileLines = new[]
        {
            "leading orphan",
            Header("alpha"),
            Header("bravo"),
            "  at Frame1",
            "  at Frame2",
        };

        var forward = DebugLogEntryParser.Parse(fileLines);
        var reverse = ReverseParse(fileLines.Reverse());

        Assert.Equal(forward.Reverse(), reverse);
    }

    private static string Header(string message) => HeaderAt(Constants.DebugLogTestTimestamp, message);

    private static string HeaderAt(string timestamp, string message) =>
        $"[{timestamp}] [{Constants.DebugLogTestThreadId}] [{nameof(LogLevel.Information)}] {message}";

    private static IReadOnlyList<DebugLogEntry> ReverseParse(IEnumerable<string> newestFirstLines)
    {
        var parser = new DebugLogEntryReverseStreamParser();
        var entries = new List<DebugLogEntry>();

        foreach (var line in newestFirstLines)
        {
            var emitted = parser.AddLine(line);

            if (emitted is not null) { entries.Add(emitted); }
        }

        var final = parser.Flush();

        if (final is not null) { entries.Add(final); }

        return entries;
    }
}
