// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Export;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace EventLogExpert.Runtime.Tests.Export;

public sealed class TabularExportWriterTests
{
    [Fact]
    [SuppressMessage("Usage", "xUnit1051", Justification = "Test exercises cancellation with a test-controlled token.")]
    public async Task Cancellation_StopsEnumerationEarly_AndLeavesStreamOpen()
    {
        using CancellationTokenSource cancellation = new();
        RecordingStream stream = new();
        CountingRowSource source = new(rowCount: 100)
        {
            AfterRowProduced = index =>
            {
                if (index == 2)
                {
                    cancellation.Cancel();
                }
            }
        };
        TabularExportWriter writer = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await writer.WriteAsync(
                stream, ExportFormat.Csv, ["A", "B"], source.GetRowsAsync(), cancellation.Token));

        Assert.True(source.RowsProduced <= 4);
        Assert.False(stream.WasDisposed);
    }

    [Fact]
    public async Task ConcurrentExports_ToSeparateStreams_DoNotShareState()
    {
        TabularExportWriter writer = new();
        using MemoryStream first = new();
        using MemoryStream second = new();

        Task firstExport = writer.WriteAsync(
            first, ExportFormat.Csv, ["A"], ExportTestHelpers.ToAsync([["first"]]), TestContext.Current.CancellationToken);
        Task secondExport = writer.WriteAsync(
            second, ExportFormat.Csv, ["B"], ExportTestHelpers.ToAsync([["second"]]), TestContext.Current.CancellationToken);

        await Task.WhenAll(firstExport, secondExport);

        Assert.Equal("A\r\nfirst\r\n", ExportTestHelpers.DecodeWithoutBom(first.ToArray()));
        Assert.Equal("B\r\nsecond\r\n", ExportTestHelpers.DecodeWithoutBom(second.ToArray()));
    }

    [Fact]
    public async Task DuplicateHeaders_Throws()
    {
        using MemoryStream stream = new();
        TabularExportWriter writer = new();

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await writer.WriteAsync(
                stream, ExportFormat.Csv, ["A", "A"], ExportTestHelpers.ToAsync([]), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EmptyHeaders_Throws()
    {
        using MemoryStream stream = new();
        TabularExportWriter writer = new();

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await writer.WriteAsync(
                stream, ExportFormat.Csv, [], ExportTestHelpers.ToAsync([]), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("A", null)]
    [InlineData("A", "")]
    public async Task HeaderWithNullOrEmptyElement_Throws(string first, string? second)
    {
        using MemoryStream stream = new();
        TabularExportWriter writer = new();

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await writer.WriteAsync(
                stream, ExportFormat.Csv, [first, second!], ExportTestHelpers.ToAsync([]), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LargeCsvInput_StreamsIncrementally_WithoutBufferingWholeOutput() =>
        await AssertStreamsIncrementally(ExportFormat.Csv);

    [Fact]
    public async Task LargeJsonInput_StreamsIncrementally_WithoutBufferingWholeOutput() =>
        await AssertStreamsIncrementally(ExportFormat.Json);

    [Fact]
    public async Task NullDestination_Throws()
    {
        TabularExportWriter writer = new();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await writer.WriteAsync(
                null!, ExportFormat.Csv, ["A"], ExportTestHelpers.ToAsync([]), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NullRows_Throws()
    {
        using MemoryStream stream = new();
        TabularExportWriter writer = new();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await writer.WriteAsync(stream, ExportFormat.Csv, ["A"], null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    [SuppressMessage("Usage", "xUnit1051", Justification = "Test exercises a pre-cancelled token.")]
    public async Task PreCanceledToken_WritesNoBytes()
    {
        RecordingStream stream = new();
        TabularExportWriter writer = new();
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await writer.WriteAsync(
                stream, ExportFormat.Csv, ["A"], ExportTestHelpers.ToAsync([["1"]]), cancellation.Token));

        Assert.Equal(0, stream.TotalBytesWritten);
    }

    [Fact]
    public async Task RowWithWrongCellCount_Throws()
    {
        using MemoryStream stream = new();
        TabularExportWriter writer = new();

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await writer.WriteAsync(
                stream, ExportFormat.Csv, ["A", "B"],
                ExportTestHelpers.ToAsync([["only-one"]]), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SuccessfulExport_LeavesDestinationStreamOpen()
    {
        RecordingStream stream = new();
        TabularExportWriter writer = new();

        await writer.WriteAsync(
            stream, ExportFormat.Csv, ["A"], ExportTestHelpers.ToAsync([["1"]]), TestContext.Current.CancellationToken);

        Assert.False(stream.WasDisposed);
    }

    [Fact]
    public async Task UnsupportedFormat_Throws()
    {
        using MemoryStream stream = new();
        TabularExportWriter writer = new();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await writer.WriteAsync(
                stream, (ExportFormat)999, ["A"], ExportTestHelpers.ToAsync([]), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WritesCsv_EndToEnd()
    {
        using MemoryStream stream = new();
        TabularExportWriter writer = new();

        await writer.WriteAsync(
            stream, ExportFormat.Csv, ["A", "B"],
            ExportTestHelpers.ToAsync([["1", "2"], ["3", "4"]]), TestContext.Current.CancellationToken);

        Assert.Equal("A,B\r\n1,2\r\n3,4\r\n", ExportTestHelpers.DecodeWithoutBom(stream.ToArray()));
    }

    [Fact]
    public async Task WritesJson_EndToEnd()
    {
        using MemoryStream stream = new();
        TabularExportWriter writer = new();

        await writer.WriteAsync(
            stream, ExportFormat.Json, ["A", "B"],
            ExportTestHelpers.ToAsync([["1", "2"], ["3", "4"]]), TestContext.Current.CancellationToken);

        using JsonDocument document = JsonDocument.Parse(new ReadOnlyMemory<byte>(stream.ToArray()));
        Assert.Equal(2, document.RootElement.GetArrayLength());
        Assert.Equal("1", document.RootElement[0].GetProperty("A").GetString());
        Assert.Equal("4", document.RootElement[1].GetProperty("B").GetString());
    }

    private static async Task AssertStreamsIncrementally(ExportFormat format)
    {
        RecordingStream stream = new();
        TabularExportWriter writer = new();

        await writer.WriteAsync(
            stream, format, ["A", "B"],
            ExportTestHelpers.ToAsync(ExportTestHelpers.GenerateRows(20_000)), TestContext.Current.CancellationToken);

        Assert.True(stream.WriteCallCount > 1);
        Assert.True(stream.MaxSingleWriteBytes < 256 * 1024);
    }
}
