// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.IO.Compression;
using System.Text;

namespace EventLogExpert.Eventing.Tests.TestUtils;

public static class CompressionTestUtils
{
    public static byte[] CompressString(string value)
    {
        var buffer = Encoding.UTF8.GetBytes(value);
        using var memoryStream = new MemoryStream();

        using (var gZipStream = new GZipStream(memoryStream, CompressionLevel.SmallestSize))
        {
            gZipStream.Write(buffer, 0, buffer.Length);
        }

        return memoryStream.ToArray();
    }

    public static CompressionTestData CreateBasicTestData() =>
        new()
        {
            Name = "Test",
            Value = 42,
            Items = ["Item1", "Item2", "Item3"]
        };

    public static CompressionTestData CreateLargeTestData() =>
        new()
        {
            Name = new string('A', 10000),
            Value = 42,
            Items = Enumerable.Range(0, 1000).Select(i => $"Item{i}").ToList()
        };

    public static NestedCompressionTestData CreateNestedTestData() =>
        new()
        {
            Id = 1,
            Child = new CompressionTestData
            {
                Name = "Child",
                Value = 100,
                Items = ["A", "B"]
            }
        };

    public static List<CompressionTestData> CreateTestDataList() =>
    [
        new() { Name = "First", Value = 1, Items = ["A"] },
        new() { Name = "Second", Value = 2, Items = ["B", "C"] },
        new() { Name = "Third", Value = 3, Items = [] }
    ];

    public static CompressionTestData CreateTestDataWithSpecialCharacters() =>
        new()
        {
            Name = "Test with special chars: <>&\"'\\日本語🎉",
            Value = int.MaxValue,
            Items = ["Line1\nLine2", "Tab\tSeparated", "Quote\"Test"]
        };
}

public class CompressionTestData
{
    public List<string>? Items { get; init; }

    public string? Name { get; init; }

    public int Value { get; init; }
}

public class NestedCompressionTestData
{
    public CompressionTestData? Child { get; init; }

    public int Id { get; init; }
}
