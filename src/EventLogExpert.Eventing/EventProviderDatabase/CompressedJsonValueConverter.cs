// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace EventLogExpert.Eventing.EventProviderDatabase;

public class CompressedJsonValueConverter<T> : ValueConverter<T, byte[]> where T : class
{
    public CompressedJsonValueConverter() :
      base(v => ConvertToCompressedJson(v), v => ConvertFromCompressedJson(v))
    { }

    public static byte[] ConvertToCompressedJson(T value)
    {
        var json = JsonSerializer.Serialize(value);
        var buffer = Encoding.UTF8.GetBytes(json);
        MemoryStream memoryStream = new MemoryStream();
        using (GZipStream gZipStream = new GZipStream(memoryStream, CompressionLevel.SmallestSize))
        {
            gZipStream.Write(buffer, 0, buffer.Length);
        }

        return memoryStream.ToArray();
    }

    public static T ConvertFromCompressedJson(byte[] value)
    {
        using (MemoryStream memoryStream = new MemoryStream(value))
        {
            using (GZipStream gZipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
            {
                using (StreamReader streamReader = new StreamReader(gZipStream))
                {
                    return JsonSerializer.Deserialize<T>(streamReader.ReadToEnd());
                }
            }
        }
    }

    private static T ConvertFromJson(string value)
    {
        return JsonSerializer.Deserialize<T>(value);
    }
}
