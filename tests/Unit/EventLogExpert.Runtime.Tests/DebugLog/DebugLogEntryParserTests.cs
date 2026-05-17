// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.DebugLog;
using EventLogExpert.Runtime.Tests.TestUtils.Constants;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace EventLogExpert.Runtime.Tests.DebugLog;

public sealed class DebugLogEntryParserTests
{
    [Fact]
    public void Parse_WhenContinuationHasBareDateHeaderShape_ShouldNotSplitFromPriorEntry()
    {
        // Arrange
        var firstLine = BuildLine(
            Constants.DebugLogTestTimestamp,
            Constants.DebugLogTestThreadId,
            nameof(LogLevel.Information),
            Constants.DebugLogFirstMessage);
        var continuationLine = $"[2026-04-29] [12] [{nameof(LogLevel.Error)}] payload that mentions a bracketed date";

        // Act
        var entries = DebugLogEntryParser.Parse([firstLine, continuationLine]);

        // Assert
        var entry = Assert.Single(entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal($"{Constants.DebugLogFirstMessage}\n{continuationLine}", entry.Message);
        Assert.Equal($"{firstLine}\n{continuationLine}", entry.RawLine);
    }

    [Fact]
    public void Parse_WhenContinuationLineFollowsEntry_ShouldFoldIntoPrevious()
    {
        // Arrange
        var firstLine = BuildLine(
            Constants.DebugLogTestTimestamp,
            Constants.DebugLogTestThreadId,
            nameof(LogLevel.Error),
            Constants.DebugLogFirstMessage);
        var continuationLine = "  at SomeMethod() in SomeFile.cs:line 42";

        // Act
        var entries = DebugLogEntryParser.Parse([firstLine, continuationLine]);

        // Assert
        var entry = Assert.Single(entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal($"{Constants.DebugLogFirstMessage}\n{continuationLine}", entry.Message);
        Assert.Equal($"{firstLine}\n{continuationLine}", entry.RawLine);
    }

    [Fact]
    public void Parse_WhenContinuationLineHasNoPriorEntry_ShouldYieldStandaloneEntry()
    {
        // Arrange
        var orphan = "stray line with no prefix";

        // Act
        var entries = DebugLogEntryParser.Parse([orphan]);

        // Assert
        var entry = Assert.Single(entries);
        Assert.Null(entry.Timestamp);
        Assert.Null(entry.ThreadId);
        Assert.Null(entry.Level);
        Assert.Equal(orphan, entry.Message);
        Assert.Equal(orphan, entry.RawLine);
    }

    [Fact]
    public void Parse_WhenEmptyInput_ShouldReturnEmpty()
    {
        // Act
        var entries = DebugLogEntryParser.Parse([]);

        // Assert
        Assert.Empty(entries);
    }

    [Fact]
    public void Parse_WhenLevelNameLowerCase_ShouldStillRecognize()
    {
        // Arrange
        var line = BuildLine(
            Constants.DebugLogTestTimestamp,
            Constants.DebugLogTestThreadId,
            "warning",
            Constants.DebugLogTestMessage);

        // Act
        var entries = DebugLogEntryParser.Parse([line]);

        // Assert
        var entry = Assert.Single(entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
    }

    [Fact]
    public void Parse_WhenLevelNameUnknown_ShouldTreatAsContinuation()
    {
        // Arrange
        var firstLine = BuildLine(
            Constants.DebugLogTestTimestamp,
            Constants.DebugLogTestThreadId,
            nameof(LogLevel.Information),
            Constants.DebugLogFirstMessage);
        var malformed = $"[{Constants.DebugLogTestTimestamp}] [{Constants.DebugLogTestThreadId}] [Bogus] payload";

        // Act
        var entries = DebugLogEntryParser.Parse([firstLine, malformed]);

        // Assert
        var entry = Assert.Single(entries);
        Assert.Equal($"{Constants.DebugLogFirstMessage}\n{malformed}", entry.Message);
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public void Parse_WhenLogLevelName_ShouldRecognize(LogLevel level)
    {
        // Arrange
        var line = BuildLine(
            Constants.DebugLogTestTimestamp,
            Constants.DebugLogTestThreadId,
            level.ToString(),
            Constants.DebugLogTestMessage);

        // Act
        var entries = DebugLogEntryParser.Parse([line]);

        // Assert
        var entry = Assert.Single(entries);
        Assert.Equal(level, entry.Level);
    }

    [Fact]
    public void Parse_WhenMessageBodyEmpty_ShouldStillParseEntry()
    {
        // Arrange
        var line = BuildLine(
            Constants.DebugLogTestTimestamp,
            Constants.DebugLogTestThreadId,
            nameof(LogLevel.Information),
            string.Empty);

        // Act
        var entries = DebugLogEntryParser.Parse([line]);

        // Assert
        var entry = Assert.Single(entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(string.Empty, entry.Message);
        Assert.Equal(line, entry.RawLine);
    }

    [Fact]
    public void Parse_WhenMessageContainsBracketTokens_ShouldKeepThemInMessage()
    {
        // Arrange
        var line = BuildLine(
            Constants.DebugLogTestTimestamp,
            Constants.DebugLogTestThreadId,
            nameof(LogLevel.Warning),
            "[Custom] tag prefix and [Information] later");

        // Act
        var entries = DebugLogEntryParser.Parse([line]);

        // Assert
        var entry = Assert.Single(entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("[Custom] tag prefix and [Information] later", entry.Message);
    }

    [Fact]
    public void Parse_WhenMultipleContinuationLines_ShouldFoldAllIntoPrevious()
    {
        // Arrange
        var firstLine = BuildLine(
            Constants.DebugLogTestTimestamp,
            Constants.DebugLogTestThreadId,
            nameof(LogLevel.Critical),
            "Unhandled exception:");
        string[] stackTrace =
        [
            "System.InvalidOperationException: Bad state",
            "   at A.B.C() in File.cs:line 1",
            "   at D.E.F() in File.cs:line 2"
        ];

        // Act
        var entries = DebugLogEntryParser.Parse([firstLine, .. stackTrace]);

        // Assert
        var entry = Assert.Single(entries);
        Assert.Equal(LogLevel.Critical, entry.Level);
        Assert.Equal($"Unhandled exception:\n{string.Join('\n', stackTrace)}", entry.Message);
        Assert.Equal($"{firstLine}\n{string.Join('\n', stackTrace)}", entry.RawLine);
    }

    [Fact]
    public void Parse_WhenMultipleEntriesEachWithContinuations_ShouldFoldContinuationsIntoTheirRespectiveEntry()
    {
        // Arrange
        var firstStart = BuildLine(
            Constants.DebugLogTestTimestamp,
            Constants.DebugLogTestThreadId,
            nameof(LogLevel.Error),
            Constants.DebugLogFirstMessage);
        var firstContinuation = "   at A.B.C() in File.cs:line 1";

        var secondStart = BuildLine(
            Constants.DebugLogTestTimestamp,
            Constants.DebugLogTestThreadId,
            nameof(LogLevel.Warning),
            Constants.DebugLogSecondMessage);
        var secondContinuationOne = "   at D.E.F() in File.cs:line 2";
        var secondContinuationTwo = "   at G.H.I() in File.cs:line 3";

        // Act
        var entries = DebugLogEntryParser.Parse(
            [firstStart, firstContinuation, secondStart, secondContinuationOne, secondContinuationTwo]);

        // Assert
        Assert.Equal(2, entries.Count);

        Assert.Equal(LogLevel.Error, entries[0].Level);
        Assert.Equal($"{Constants.DebugLogFirstMessage}\n{firstContinuation}", entries[0].Message);
        Assert.Equal($"{firstStart}\n{firstContinuation}", entries[0].RawLine);

        Assert.Equal(LogLevel.Warning, entries[1].Level);
        Assert.Equal(
            $"{Constants.DebugLogSecondMessage}\n{secondContinuationOne}\n{secondContinuationTwo}",
            entries[1].Message);
        Assert.Equal(
            $"{secondStart}\n{secondContinuationOne}\n{secondContinuationTwo}",
            entries[1].RawLine);
    }

    [Fact]
    public void Parse_WhenMultipleEntries_ShouldReturnInOrder()
    {
        // Arrange
        string[] lines =
        [
            BuildLine(Constants.DebugLogTestTimestamp, Constants.DebugLogTestThreadId, nameof(LogLevel.Trace), Constants.DebugLogFirstMessage),
            BuildLine(Constants.DebugLogTestTimestamp, Constants.DebugLogTestThreadId, nameof(LogLevel.Information), Constants.DebugLogSecondMessage),
            BuildLine(Constants.DebugLogTestTimestamp, Constants.DebugLogTestThreadId, nameof(LogLevel.Error), Constants.DebugLogThirdMessage)
        ];

        // Act
        var entries = DebugLogEntryParser.Parse(lines);

        // Assert
        Assert.Equal(3, entries.Count);
        Assert.Equal(LogLevel.Trace, entries[0].Level);
        Assert.Equal(Constants.DebugLogFirstMessage, entries[0].Message);
        Assert.Equal(LogLevel.Information, entries[1].Level);
        Assert.Equal(Constants.DebugLogSecondMessage, entries[1].Message);
        Assert.Equal(LogLevel.Error, entries[2].Level);
        Assert.Equal(Constants.DebugLogThirdMessage, entries[2].Message);
    }

    [Fact]
    public void Parse_WhenOrphanFollowedByContinuationLines_ShouldFoldContinuationsIntoOrphan()
    {
        // Arrange
        var orphanFirstLine = "stray prelude with no prefix";
        var continuationOne = "more stray content";
        var continuationTwo = "and another stray line";

        // Act
        var entries = DebugLogEntryParser.Parse([orphanFirstLine, continuationOne, continuationTwo]);

        // Assert
        var entry = Assert.Single(entries);
        Assert.Null(entry.Timestamp);
        Assert.Null(entry.ThreadId);
        Assert.Null(entry.Level);
        Assert.Equal($"{orphanFirstLine}\n{continuationOne}\n{continuationTwo}", entry.Message);
        Assert.Equal($"{orphanFirstLine}\n{continuationOne}\n{continuationTwo}", entry.RawLine);
    }

    [Fact]
    public void Parse_WhenStandardLine_ShouldReturnEntryWithAllFields()
    {
        // Arrange
        var line = BuildLine(
            Constants.DebugLogTestTimestamp,
            Constants.DebugLogTestThreadId,
            nameof(LogLevel.Information),
            Constants.DebugLogTestMessage);

        // Act
        var entries = DebugLogEntryParser.Parse([line]);

        // Assert
        var entry = Assert.Single(entries);
        Assert.Equal(
            DateTimeOffset.Parse(Constants.DebugLogTestTimestamp, CultureInfo.InvariantCulture),
            entry.Timestamp);
        Assert.Equal(Constants.DebugLogTestThreadId, entry.ThreadId);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(Constants.DebugLogTestMessage, entry.Message);
        Assert.Equal(line, entry.RawLine);
    }

    [Fact]
    public void Parse_WhenThreadIdMalformed_ShouldTreatAsContinuation()
    {
        // Arrange
        var firstLine = BuildLine(
            Constants.DebugLogTestTimestamp,
            Constants.DebugLogTestThreadId,
            nameof(LogLevel.Information),
            Constants.DebugLogFirstMessage);
        var malformed = $"[{Constants.DebugLogTestTimestamp}] [abc] [Information] payload";

        // Act
        var entries = DebugLogEntryParser.Parse([firstLine, malformed]);

        // Assert
        var entry = Assert.Single(entries);
        Assert.Equal($"{Constants.DebugLogFirstMessage}\n{malformed}", entry.Message);
    }

    [Fact]
    public void Parse_WhenTimestampMalformed_ShouldTreatAsContinuation()
    {
        // Arrange
        var firstLine = BuildLine(
            Constants.DebugLogTestTimestamp,
            Constants.DebugLogTestThreadId,
            nameof(LogLevel.Information),
            Constants.DebugLogFirstMessage);
        var malformed = "[not-a-timestamp] [12] [Information] payload";

        // Act
        var entries = DebugLogEntryParser.Parse([firstLine, malformed]);

        // Assert
        var entry = Assert.Single(entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal($"{Constants.DebugLogFirstMessage}\n{malformed}", entry.Message);
    }

    [Fact]
    public void TryParseLine_WhenNoPrefix_ShouldReturnFalse()
    {
        // Act
        var success = DebugLogEntryParser.TryParseLine("orphan line", out var entry);

        // Assert
        Assert.False(success);
        Assert.Null(entry);
    }

    [Fact]
    public void TryParseLine_WhenValidPrefix_ShouldReturnTrueAndEntry()
    {
        // Arrange
        var line = BuildLine(
            Constants.DebugLogTestTimestamp,
            Constants.DebugLogTestThreadId,
            nameof(LogLevel.Information),
            Constants.DebugLogTestMessage);

        // Act
        var success = DebugLogEntryParser.TryParseLine(line, out var entry);

        // Assert
        Assert.True(success);
        Assert.NotNull(entry);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(Constants.DebugLogTestMessage, entry.Message);
        Assert.Equal(line, entry.RawLine);
    }

    private static string BuildLine(string timestamp, int threadId, string level, string message) =>
        $"[{timestamp}] [{threadId}] [{level}] {message}";
}

public sealed class DebugLogEntryStreamParserTests
{
    [Fact]
    public void AddLine_AfterFlush_ShouldStartFreshState()
    {
        // Arrange
        var parser = new DebugLogEntryStreamParser();
        parser.AddLine(BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage));
        parser.Flush();

        // Act
        var emitted = parser.AddLine(BuildLine(LogLevel.Warning, Constants.DebugLogSecondMessage));

        // Assert
        Assert.Null(emitted);

        var final = parser.Flush();

        Assert.NotNull(final);
        Assert.Equal(LogLevel.Warning, final.Level);
        Assert.Equal(Constants.DebugLogSecondMessage, final.Message);
    }

    [Fact]
    public void AddLine_WhenContinuationLineHasNoPending_ShouldStartStandaloneEntry()
    {
        // Arrange
        var parser = new DebugLogEntryStreamParser();
        const string Orphan = "stray line with no prefix";

        // Act
        var emitted = parser.AddLine(Orphan);

        // Assert
        Assert.Null(emitted);

        var final = parser.Flush();

        Assert.NotNull(final);
        Assert.Null(final.Level);
        Assert.Null(final.Timestamp);
        Assert.Null(final.ThreadId);
        Assert.Equal(Orphan, final.Message);
        Assert.Equal(Orphan, final.RawLine);
    }

    [Fact]
    public void AddLine_WhenContinuationLinesPrecedeNextHeader_ShouldFoldIntoEmittedEntry()
    {
        // Arrange
        var parser = new DebugLogEntryStreamParser();
        var firstLine = BuildLine(LogLevel.Error, Constants.DebugLogFirstMessage);
        const string Stack1 = "  at System.Foo.Bar()";
        const string Stack2 = "  at System.Baz.Qux()";
        var secondLine = BuildLine(LogLevel.Information, Constants.DebugLogSecondMessage);

        // Act
        parser.AddLine(firstLine);
        Assert.Null(parser.AddLine(Stack1));
        Assert.Null(parser.AddLine(Stack2));
        var emitted = parser.AddLine(secondLine);

        // Assert
        Assert.NotNull(emitted);
        Assert.Equal($"{Constants.DebugLogFirstMessage}\n{Stack1}\n{Stack2}", emitted.Message);
        Assert.Equal($"{firstLine}\n{Stack1}\n{Stack2}", emitted.RawLine);
    }

    [Fact]
    public void AddLine_WhenFirstLineIsHeader_ShouldBufferAndReturnNull()
    {
        // Arrange
        var parser = new DebugLogEntryStreamParser();
        var line = BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage);

        // Act
        var emitted = parser.AddLine(line);

        // Assert
        Assert.Null(emitted);
    }

    [Fact]
    public void AddLine_WhenNullLine_ShouldThrow()
    {
        // Arrange
        var parser = new DebugLogEntryStreamParser();

        // Act + Assert
        Assert.Throws<ArgumentNullException>(() => parser.AddLine(null!));
    }

    [Fact]
    public void AddLine_WhenSecondHeaderArrives_ShouldEmitFirstEntry()
    {
        // Arrange
        var parser = new DebugLogEntryStreamParser();
        var firstLine = BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage);
        var secondLine = BuildLine(LogLevel.Warning, Constants.DebugLogSecondMessage);

        // Act
        parser.AddLine(firstLine);
        var emitted = parser.AddLine(secondLine);

        // Assert
        Assert.NotNull(emitted);
        Assert.Equal(LogLevel.Information, emitted.Level);
        Assert.Equal(Constants.DebugLogFirstMessage, emitted.Message);
        Assert.Equal(firstLine, emitted.RawLine);
    }

    [Fact]
    public void AddLine_WhenSubsequentHeaderHasOlderTimestamp_ShouldStartNewEntry()
    {
        // Arrange
        var parser = new DebugLogEntryStreamParser();
        var pendingLine = BuildLine(Constants.DebugLogTestTimestamp, LogLevel.Error, Constants.DebugLogFirstMessage);
        var olderHeaderLine = BuildLine(Constants.DebugLogOlderTimestamp, LogLevel.Information, Constants.DebugLogSecondMessage);

        // Act
        parser.AddLine(pendingLine);
        var emittedOnSecondLine = parser.AddLine(olderHeaderLine);
        var final = parser.Flush();

        // Assert
        Assert.NotNull(emittedOnSecondLine);
        Assert.Equal(LogLevel.Error, emittedOnSecondLine.Level);
        Assert.Equal(Constants.DebugLogFirstMessage, emittedOnSecondLine.Message);
        Assert.Equal(pendingLine, emittedOnSecondLine.RawLine);
        Assert.NotNull(final);
        Assert.Equal(LogLevel.Information, final.Level);
        Assert.Equal(Constants.DebugLogSecondMessage, final.Message);
        Assert.Equal(olderHeaderLine, final.RawLine);
    }

    [Fact]
    public void Flush_WhenCalledTwice_SecondReturnsNull()
    {
        // Arrange
        var parser = new DebugLogEntryStreamParser();
        parser.AddLine(BuildLine(LogLevel.Information, Constants.DebugLogFirstMessage));

        // Act + Assert
        Assert.NotNull(parser.Flush());
        Assert.Null(parser.Flush());
    }

    [Fact]
    public void Flush_WhenNoLinesAdded_ShouldReturnNull()
    {
        // Arrange
        var parser = new DebugLogEntryStreamParser();

        // Act + Assert
        Assert.Null(parser.Flush());
    }

    [Fact]
    public void Flush_WhenPendingHeaderHasContinuations_ShouldFoldThemAndReturnEntry()
    {
        // Arrange
        var parser = new DebugLogEntryStreamParser();
        var headerLine = BuildLine(LogLevel.Error, Constants.DebugLogFirstMessage);
        const string Stack = "  at System.Foo.Bar()";

        // Act
        parser.AddLine(headerLine);
        parser.AddLine(Stack);
        var final = parser.Flush();

        // Assert
        Assert.NotNull(final);
        Assert.Equal($"{Constants.DebugLogFirstMessage}\n{Stack}", final.Message);
        Assert.Equal($"{headerLine}\n{Stack}", final.RawLine);
    }

    private static string BuildLine(LogLevel level, string message) =>
        $"[{Constants.DebugLogTestTimestamp}] [{Constants.DebugLogTestThreadId}] [{level}] {message}";

    private static string BuildLine(string timestamp, LogLevel level, string message) =>
        $"[{timestamp}] [{Constants.DebugLogTestThreadId}] [{level}] {message}";
}
