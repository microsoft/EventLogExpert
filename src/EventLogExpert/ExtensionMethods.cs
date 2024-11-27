// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;

namespace EventLogExpert;

internal static class ExtensionMethods
{
    internal static void AndForget(this Task task, ITraceLogger? logger = null)
    {
        if (!task.IsCompleted || task.IsFaulted) { _ = ForgetAwaited(task, logger); }

        static async Task ForgetAwaited(Task task, ITraceLogger? logger = null)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.Trace(ex.Message);
            }
        }
    }

    internal static DateTime ConvertTimeZone(this DateTime time, TimeZoneInfo? destinationTime) =>
        destinationTime is null ? time : TimeZoneInfo.ConvertTimeFromUtc(time, destinationTime);

    internal static DateTime ConvertTimeZoneToUtc(this DateTime time, TimeZoneInfo? destinationTime) =>
        destinationTime is null ? time : TimeZoneInfo.ConvertTimeToUtc(time, destinationTime);

    internal static string GetEventKeywords(this IEnumerable<string> keywords)
    {
        StringBuilder sb = new("Keywords:");

        foreach (var keyword in keywords) { sb.Append($" {keyword}"); }

        return sb.ToString();
    }

    internal static string ToFullString(this Enum value)
    {
        var memberAttribute = value.GetType().GetField(value.ToString())?
            .GetCustomAttribute(typeof(EnumMemberAttribute)) as EnumMemberAttribute;

        return memberAttribute?.Value ?? value.ToString();
    }
}
