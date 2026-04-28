// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.IO.Compression;
using System.Text.Json;

namespace EventLogExpert.Eventing.EventProviderDatabase;

public class CompressedJsonValueConverter<T>() : ValueConverter<T, byte[]>(v => ConvertToCompressedJson(v),
    v => ConvertFromCompressedJson(v))
    where T : class
{
    public static byte[] ConvertToCompressedJson(T value)
    {
        using MemoryStream memoryStream = new();

        using (GZipStream gZipStream = new(memoryStream, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            JsonSerializer.Serialize(gZipStream, value, ProviderJsonSerializerOptions.Default);
        }

        return memoryStream.ToArray();
    }

    public static T ConvertFromCompressedJson(byte[] value)
    {
        using MemoryStream memoryStream = new(value);
        using GZipStream gZipStream = new(memoryStream, CompressionMode.Decompress);

        return JsonSerializer.Deserialize<T>(gZipStream, ProviderJsonSerializerOptions.Default)
            ?? throw new JsonException($"Failed to deserialize compressed JSON to type {typeof(T).Name}. The deserialized value was null.");
    }
}
