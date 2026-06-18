// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Export;

namespace EventLogExpert.Runtime.Tests.Export;

public sealed class CsvExportSinkTests
{
    [Fact]
    public async Task Complete_BeforeHeader_Throws()
    {
        using MemoryStream stream = new();
        await using CsvExportSink sink = new(stream, ["A"]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await sink.CompleteAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisposeAsync_LeavesDestinationStreamOpen()
    {
        using MemoryStream stream = new();
        CsvExportSink sink = new(stream, ["A"]);

        await sink.WriteHeaderAsync(TestContext.Current.CancellationToken);
        await sink.CompleteAsync(TestContext.Current.CancellationToken);
        await sink.DisposeAsync();

        Assert.True(stream.CanWrite);
    }

    [Fact]
    public async Task EmptyStringCell_BecomesEmptyField()
    {
        byte[] bytes = await WriteCsvAsync(["A", "B"], ["", "x"]);

        Assert.Equal("A,B\r\n,x\r\n", ExportTestHelpers.DecodeWithoutBom(bytes));
    }

    [Fact]
    public async Task FieldThatIsOnlyADoubleQuote_IsQuotedAndDoubled()
    {
        byte[] bytes = await WriteCsvAsync(["X"], ["\""]);

        Assert.Equal("X\r\n\"\"\"\"\r\n", ExportTestHelpers.DecodeWithoutBom(bytes));
    }

    [Fact]
    public async Task FieldWithComma_IsQuoted()
    {
        byte[] bytes = await WriteCsvAsync(["A", "B"], ["a,b", "x"]);

        Assert.Equal("A,B\r\n\"a,b\",x\r\n", ExportTestHelpers.DecodeWithoutBom(bytes));
    }

    [Fact]
    public async Task FieldWithDoubleQuote_IsQuotedAndDoubled()
    {
        byte[] bytes = await WriteCsvAsync(["A", "B"], ["a\"b", "x"]);

        Assert.Equal("A,B\r\n\"a\"\"b\",x\r\n", ExportTestHelpers.DecodeWithoutBom(bytes));
    }

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    [InlineData("a\r\nb")]
    public async Task FieldWithLineBreak_IsQuoted(string value)
    {
        byte[] bytes = await WriteCsvAsync(["X"], [value]);

        Assert.Equal($"X\r\n\"{value}\"\r\n", ExportTestHelpers.DecodeWithoutBom(bytes));
    }

    [Fact]
    public async Task Header_EmitsUtf8ByteOrderMark()
    {
        byte[] bytes = await WriteCsvAsync(["A", "B"], ["1", "2"]);

        Assert.True(ExportTestHelpers.StartsWithUtf8Bom(bytes));
    }

    [Fact]
    public async Task HeaderAndRow_AreCrlfTerminated()
    {
        byte[] bytes = await WriteCsvAsync(["Level", "Source"], ["Information", "Kernel"]);

        Assert.Equal("Level,Source\r\nInformation,Kernel\r\n", ExportTestHelpers.DecodeWithoutBom(bytes));
    }

    [Fact]
    public async Task HeaderOnly_WithNoRows_WritesHeaderLineOnly()
    {
        byte[] bytes = await WriteCsvAsync(["A", "B"]);

        Assert.Equal("A,B\r\n", ExportTestHelpers.DecodeWithoutBom(bytes));
    }

    [Fact]
    public async Task HeaderWithSpecialCharacters_IsQuoted()
    {
        byte[] bytes = await WriteCsvAsync(["a,b", "c"], ["1", "2"]);

        Assert.Equal("\"a,b\",c\r\n1,2\r\n", ExportTestHelpers.DecodeWithoutBom(bytes));
    }

    [Fact]
    public async Task LeadingAndTrailingWhitespace_IsPreservedWithoutQuoting()
    {
        byte[] bytes = await WriteCsvAsync(["A", "B"], [" a ", "x"]);

        Assert.Equal("A,B\r\n a ,x\r\n", ExportTestHelpers.DecodeWithoutBom(bytes));
    }

    [Fact]
    public async Task NullCell_BecomesEmptyField()
    {
        byte[] bytes = await WriteCsvAsync(["A", "B"], [null, "x"]);

        Assert.Equal("A,B\r\n,x\r\n", ExportTestHelpers.DecodeWithoutBom(bytes));
    }

    [Fact]
    public async Task UnicodeCharacters_RoundTripAsUtf8()
    {
        byte[] bytes = await WriteCsvAsync(["A", "B"], ["café 日本語", "x"]);

        Assert.Equal("A,B\r\ncafé 日本語,x\r\n", ExportTestHelpers.DecodeWithoutBom(bytes));
    }

    [Fact]
    public async Task WriteRow_BeforeHeader_Throws()
    {
        using MemoryStream stream = new();
        await using CsvExportSink sink = new(stream, ["A"]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await sink.WriteRowAsync(["x"], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WriteRow_WithWrongCellCount_Throws()
    {
        using MemoryStream stream = new();
        await using CsvExportSink sink = new(stream, ["A", "B"]);
        await sink.WriteHeaderAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await sink.WriteRowAsync(["only-one"], TestContext.Current.CancellationToken));
    }

    private static async Task<byte[]> WriteCsvAsync(string[] headers, params IReadOnlyList<string?>[] rows)
    {
        using MemoryStream stream = new();

        await using (CsvExportSink sink = new(stream, headers))
        {
            await sink.WriteHeaderAsync(TestContext.Current.CancellationToken);

            foreach (IReadOnlyList<string?> row in rows)
            {
                await sink.WriteRowAsync(row, TestContext.Current.CancellationToken);
            }

            await sink.CompleteAsync(TestContext.Current.CancellationToken);
        }

        return stream.ToArray();
    }
}
