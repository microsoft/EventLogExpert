// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.Tests.TestUtils;
using System.Text.Json;

namespace EventLogExpert.Eventing.Tests.EventProviderDatabase;

public sealed class CompressedJsonValueConverterTests
{
    [Fact]
    public void Constructor_ShouldCreateInstance()
    {
        // Act
        var converter = new CompressedJsonValueConverter<CompressionTestData>();

        // Assert
        Assert.NotNull(converter);
    }

    [Fact]
    public void ConvertFromCompressedJson_WithEmptyCollection_ShouldReturnEmptyCollection()
    {
        // Arrange
        var originalData = new List<string>();
        var compressed = CompressedJsonValueConverter<List<string>>.ConvertToCompressedJson(originalData);

        // Act
        var result = CompressedJsonValueConverter<List<string>>.ConvertFromCompressedJson(compressed);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void ConvertFromCompressedJson_WithInvalidData_ShouldThrowException()
    {
        // Arrange
        var invalidData = new byte[] { 0x00, 0x01, 0x02, 0x03 };

        // Act & Assert
        Assert.ThrowsAny<Exception>(() =>
            CompressedJsonValueConverter<CompressionTestData>.ConvertFromCompressedJson(invalidData));
    }

    [Fact]
    public void ConvertFromCompressedJson_WithNullDeserializationResult_ShouldThrowJsonException()
    {
        // Arrange - compress a JSON null value
        var compressedNull = CompressionTestUtils.CompressString("null");

        // Act & Assert
        var exception = Assert.Throws<JsonException>(() =>
            CompressedJsonValueConverter<CompressionTestData>.ConvertFromCompressedJson(compressedNull));

        Assert.Contains("Failed to deserialize compressed JSON", exception.Message);
        Assert.Contains(nameof(CompressionTestData), exception.Message);
    }

    [Fact]
    public void ConvertToCompressedJson_ShouldProduceSmallerOutput_ForLargeData()
    {
        // Arrange
        var largeData = CompressionTestUtils.CreateLargeTestData();

        var uncompressedJson = JsonSerializer.Serialize(largeData);
        var uncompressedBytes = System.Text.Encoding.UTF8.GetBytes(uncompressedJson);

        // Act
        var compressed = CompressedJsonValueConverter<CompressionTestData>.ConvertToCompressedJson(largeData);

        // Assert
        Assert.True(compressed.Length < uncompressedBytes.Length,
            $"Compressed size ({compressed.Length}) should be smaller than uncompressed size ({uncompressedBytes.Length})");
    }

    [Fact]
    public void ConvertToCompressedJson_WithComplexObject_ShouldRoundTrip()
    {
        // Arrange
        var originalData = CompressionTestUtils.CreateBasicTestData();

        // Act
        var compressed = CompressedJsonValueConverter<CompressionTestData>.ConvertToCompressedJson(originalData);
        var decompressed = CompressedJsonValueConverter<CompressionTestData>.ConvertFromCompressedJson(compressed);

        // Assert
        Assert.NotNull(decompressed);
        Assert.Equal(originalData.Name, decompressed.Name);
        Assert.Equal(originalData.Value, decompressed.Value);
        Assert.Equal(originalData.Items, decompressed.Items);
    }

    [Fact]
    public void ConvertToCompressedJson_WithDictionary_ShouldRoundTrip()
    {
        // Arrange
        var originalData = new Dictionary<string, int>
        {
            ["one"] = 1,
            ["two"] = 2,
            ["three"] = 3
        };

        // Act
        var compressed = CompressedJsonValueConverter<Dictionary<string, int>>.ConvertToCompressedJson(originalData);
        var decompressed = CompressedJsonValueConverter<Dictionary<string, int>>.ConvertFromCompressedJson(compressed);

        // Assert
        Assert.NotNull(decompressed);
        Assert.Equal(originalData.Count, decompressed.Count);
        Assert.Equal(originalData["one"], decompressed["one"]);
        Assert.Equal(originalData["two"], decompressed["two"]);
        Assert.Equal(originalData["three"], decompressed["three"]);
    }

    [Fact]
    public void ConvertToCompressedJson_WithEmptyObject_ShouldRoundTrip()
    {
        // Arrange
        var originalData = new CompressionTestData();

        // Act
        var compressed = CompressedJsonValueConverter<CompressionTestData>.ConvertToCompressedJson(originalData);
        var decompressed = CompressedJsonValueConverter<CompressionTestData>.ConvertFromCompressedJson(compressed);

        // Assert
        Assert.NotNull(decompressed);
        Assert.Null(decompressed.Name);
        Assert.Equal(0, decompressed.Value);
        Assert.Null(decompressed.Items);
    }

    [Fact]
    public void ConvertToCompressedJson_WithNestedObjects_ShouldRoundTrip()
    {
        // Arrange
        var originalData = CompressionTestUtils.CreateNestedTestData();

        // Act
        var compressed = CompressedJsonValueConverter<NestedCompressionTestData>.ConvertToCompressedJson(originalData);
        var decompressed = CompressedJsonValueConverter<NestedCompressionTestData>.ConvertFromCompressedJson(compressed);

        // Assert
        Assert.NotNull(decompressed);
        Assert.Equal(originalData.Id, decompressed.Id);
        Assert.NotNull(decompressed.Child);
        Assert.Equal(originalData.Child!.Name, decompressed.Child.Name);
        Assert.Equal(originalData.Child.Value, decompressed.Child.Value);
        Assert.Equal(originalData.Child.Items, decompressed.Child.Items);
    }

    [Fact]
    public void ConvertToCompressedJson_WithSpecialCharacters_ShouldRoundTrip()
    {
        // Arrange
        var originalData = CompressionTestUtils.CreateTestDataWithSpecialCharacters();

        // Act
        var compressed = CompressedJsonValueConverter<CompressionTestData>.ConvertToCompressedJson(originalData);
        var decompressed = CompressedJsonValueConverter<CompressionTestData>.ConvertFromCompressedJson(compressed);

        // Assert
        Assert.NotNull(decompressed);
        Assert.Equal(originalData.Name, decompressed.Name);
        Assert.Equal(originalData.Value, decompressed.Value);
        Assert.Equal(originalData.Items, decompressed.Items);
    }

    [Fact]
    public void RoundTrip_WithList_ShouldPreserveAllElements()
    {
        // Arrange
        var originalList = CompressionTestUtils.CreateTestDataList();

        // Act
        var compressed = CompressedJsonValueConverter<List<CompressionTestData>>.ConvertToCompressedJson(originalList);
        var decompressed = CompressedJsonValueConverter<List<CompressionTestData>>.ConvertFromCompressedJson(compressed);

        // Assert
        Assert.NotNull(decompressed);
        Assert.Equal(originalList.Count, decompressed.Count);

        for (int i = 0; i < originalList.Count; i++)
        {
            Assert.Equal(originalList[i].Name, decompressed[i].Name);
            Assert.Equal(originalList[i].Value, decompressed[i].Value);
            Assert.Equal(originalList[i].Items, decompressed[i].Items);
        }
    }
}
