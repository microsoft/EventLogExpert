// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Export;
using System.Text.Json;

namespace EventLogExpert.Runtime.Tests.Export;

public sealed class JsonExportSinkTests
{
    [Fact]
    public async Task DoesNotEmitByteOrderMark()
    {
        byte[] bytes = await WriteJsonAsync(["A"], ["1"]);

        Assert.False(ExportTestHelpers.StartsWithUtf8Bom(bytes));
    }

    [Fact]
    public async Task EscapesSpecialCharacters_AndRoundTrips()
    {
        string value = "a\"b\\c\n\td";

        byte[] bytes = await WriteJsonAsync(["X"], [value]);

        using JsonDocument document = Parse(bytes);
        Assert.Equal(value, document.RootElement[0].GetProperty("X").GetString());
    }

    [Fact]
    public async Task NoRows_ProduceEmptyArray()
    {
        byte[] bytes = await WriteJsonAsync(["A", "B"]);

        using JsonDocument document = Parse(bytes);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(0, document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task NullCell_EmitsJsonNull()
    {
        byte[] bytes = await WriteJsonAsync(["A", "B"], [null, "x"]);

        using JsonDocument document = Parse(bytes);
        Assert.Equal(JsonValueKind.Null, document.RootElement[0].GetProperty("A").ValueKind);
    }

    [Fact]
    public async Task Output_IsIndented()
    {
        byte[] bytes = await WriteJsonAsync(["A"], ["1"]);

        Assert.Contains("\n", ExportTestHelpers.DecodeWithoutBom(bytes));
    }

    [Fact]
    public async Task PreservesHeaderOrder_InEachObject()
    {
        byte[] bytes = await WriteJsonAsync(["Third", "First", "Second"], ["3", "1", "2"]);

        using JsonDocument document = Parse(bytes);
        string[] propertyNames = [.. document.RootElement[0].EnumerateObject().Select(property => property.Name)];
        string[] expected = ["Third", "First", "Second"];

        Assert.Equal(expected, propertyNames);
    }

    [Fact]
    public async Task ProducesArrayOfObjects_WithHeaderProperties()
    {
        byte[] bytes = await WriteJsonAsync(["Id", "Source"], ["4624", "Security"]);

        using JsonDocument document = Parse(bytes);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(1, document.RootElement.GetArrayLength());

        JsonElement row = document.RootElement[0];
        Assert.Equal("4624", row.GetProperty("Id").GetString());
        Assert.Equal("Security", row.GetProperty("Source").GetString());
    }

    [Fact]
    public async Task UnicodeValue_RoundTrips()
    {
        string value = "café 日本語";

        byte[] bytes = await WriteJsonAsync(["X"], [value]);

        using JsonDocument document = Parse(bytes);
        Assert.Equal(value, document.RootElement[0].GetProperty("X").GetString());
    }

    [Fact]
    public async Task Values_AreEmittedAsStrings()
    {
        byte[] bytes = await WriteJsonAsync(["Id"], ["4624"]);

        using JsonDocument document = Parse(bytes);
        Assert.Equal(JsonValueKind.String, document.RootElement[0].GetProperty("Id").ValueKind);
    }

    [Fact]
    public async Task WriteRow_WithWrongCellCount_Throws()
    {
        using MemoryStream stream = new();
        await using JsonExportSink sink = new(stream, ["A", "B"]);
        await sink.WriteHeaderAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await sink.WriteRowAsync(["only-one"], TestContext.Current.CancellationToken));
    }

    private static JsonDocument Parse(byte[] utf8Json) => JsonDocument.Parse(new ReadOnlyMemory<byte>(utf8Json));

    private static async Task<byte[]> WriteJsonAsync(string[] headers, params IReadOnlyList<string?>[] rows)
    {
        using MemoryStream stream = new();

        await using (JsonExportSink sink = new(stream, headers))
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
