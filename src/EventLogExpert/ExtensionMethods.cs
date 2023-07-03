// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using System.Reflection;
using System.Runtime.Serialization;

namespace EventLogExpert;

public static class ExtensionMethods
{
    public static void AndForget(this Task task, ITraceLogger? logger = null)
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

    public static string ToFullString(this Enum value)
    {
        var memberAttribute = value.GetType().GetField(value.ToString())?
            .GetCustomAttribute(typeof(EnumMemberAttribute)) as EnumMemberAttribute;

        return memberAttribute?.Value ?? value.ToString();
    }

    internal static DateTime ConvertTimeZone(this DateTime time, TimeZoneInfo? destinationTime) =>
        destinationTime is null ? time : TimeZoneInfo.ConvertTimeFromUtc(time, destinationTime);
}
