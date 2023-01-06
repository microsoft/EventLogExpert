// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EventLogExpert.Library.EventDatabase;

public class ArrayOfLongConverter : ValueConverter<long[], string>
{
    public ArrayOfLongConverter()
        : base(
            v => string.Join(',', v),
            v => ConvertFromString(v))
    { }

    public static long[] ConvertFromString(string value)
    {
        return value.Split(',').Select(s => long.Parse(s)).ToArray();
    }
}
