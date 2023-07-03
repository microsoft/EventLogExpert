// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.
using System.Reflection;
using System.Runtime.Serialization;

namespace EventLogExpert;

public static class ExtensionMethods
{
    public static string ToFullString(this Enum value)
    {
        var memberAttribute = value.GetType().GetField(value.ToString())?
            .GetCustomAttribute(typeof(EnumMemberAttribute)) as EnumMemberAttribute;

        return memberAttribute?.Value ?? value.ToString();
    }

    internal static DateTime ConvertTimeZone(this DateTime time, TimeZoneInfo? destinationTime) =>
        destinationTime is null ? time : TimeZoneInfo.ConvertTimeFromUtc(time, destinationTime);
}
