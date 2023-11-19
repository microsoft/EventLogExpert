// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json;

namespace EventLogExpert.Eventing.EventProviderDatabase;

public sealed class JsonValueConverter<T>() : ValueConverter<T, string>(v => ConvertToJson(v), v => ConvertFromJson(v)!)
    where T : class
{
    private static string ConvertToJson(T value) => JsonSerializer.Serialize(value);

    private static T? ConvertFromJson(string value) => JsonSerializer.Deserialize<T>(value);
}
