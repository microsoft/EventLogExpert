// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace EventLogExpert.Eventing.EventProviderDatabase;

public sealed class CompressedJsonValueConverter<T>() :
    ValueConverter<T, byte[]>(v => ConvertToCompressedJson(v), v => ConvertFromCompressedJson(v)!)
    where T : class
{
    public static byte[] ConvertToCompressedJson(T value)
    {
        var json = JsonSerializer.Serialize(value);
        var buffer = Encoding.UTF8.GetBytes(json);
        MemoryStream memoryStream = new();

        using (GZipStream gZipStream = new(memoryStream, CompressionLevel.SmallestSize))
        {
            gZipStream.Write(buffer, 0, buffer.Length);
        }

        return memoryStream.ToArray();
    }

    public static T? ConvertFromCompressedJson(byte[] value)
    {
        using MemoryStream memoryStream = new(value);
        using GZipStream gZipStream = new(memoryStream, CompressionMode.Decompress);
        using StreamReader streamReader = new(gZipStream);

        return JsonSerializer.Deserialize<T>(streamReader.ReadToEnd());
    }

    private static T? ConvertFromJson(string value) => JsonSerializer.Deserialize<T>(value);
}
