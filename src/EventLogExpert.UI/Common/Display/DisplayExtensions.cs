// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Reflection;
using System.Runtime.Serialization;

namespace EventLogExpert.UI.Common.Display;

public static class DisplayExtensions
{
    public static string ToFullString(this Enum value)
    {
        var memberAttribute = value.GetType().GetField(value.ToString())?
            .GetCustomAttribute(typeof(EnumMemberAttribute)) as EnumMemberAttribute;

        return memberAttribute?.Value ?? value.ToString();
    }

    extension(DateTime time)
    {
        public DateTime ConvertTimeZone(TimeZoneInfo? destinationTime) =>
            destinationTime is null ? time : TimeZoneInfo.ConvertTimeFromUtc(time, destinationTime);

        public DateTime ConvertTimeZoneToUtc(TimeZoneInfo? destinationTime) =>
            destinationTime is null ? time : TimeZoneInfo.ConvertTimeToUtc(time, destinationTime);
    }
}
