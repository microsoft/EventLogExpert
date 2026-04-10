// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Reflection;
using System.Runtime.Serialization;

namespace EventLogExpert;

internal static class ExtensionMethods
{
    extension(DateTime time)
    {
        internal DateTime ConvertTimeZone(TimeZoneInfo? destinationTime) =>
            destinationTime is null ? time : TimeZoneInfo.ConvertTimeFromUtc(time, destinationTime);

        internal DateTime ConvertTimeZoneToUtc(TimeZoneInfo? destinationTime) =>
            destinationTime is null ? time : TimeZoneInfo.ConvertTimeToUtc(time, destinationTime);
    }

    internal static string ToFullString(this Enum value)
    {
        var memberAttribute = value.GetType().GetField(value.ToString())?
            .GetCustomAttribute(typeof(EnumMemberAttribute)) as EnumMemberAttribute;

        return memberAttribute?.Value ?? value.ToString();
    }
}
