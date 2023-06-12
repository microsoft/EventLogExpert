// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json;

namespace EventLogExpert.Eventing.EventProviderDatabase;

public class JsonValueConverter<T> : ValueConverter<T, string> where T : class
{
    public JsonValueConverter() :
      base(v => ConvertToJson(v), v => ConvertFromJson(v))
    { }

    private static string ConvertToJson(T value)
    {
        return JsonSerializer.Serialize(value);
    }

    private static T ConvertFromJson(string value)
    {
        return JsonSerializer.Deserialize<T>(value);
    }
}
