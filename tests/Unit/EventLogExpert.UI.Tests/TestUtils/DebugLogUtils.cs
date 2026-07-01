// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace EventLogExpert.UI.Tests.TestUtils;

public static class DebugLogUtils
{
    public static string BuildElevatedHelperLine(LogLevel level, string message) =>
        $"[{Constants.Constants.DebugLogTestTimestamp}] [{Constants.Constants.DebugLogTestThreadId}] [{level}] [ElevatedHelper] {message}";

    public static string BuildLine(LogLevel level, string message) =>
        $"[{Constants.Constants.DebugLogTestTimestamp}] [{Constants.Constants.DebugLogTestThreadId}] [{level}] {message}";

    public static string BuildLine(LogLevel level, string category, string message) =>
        $"[{Constants.Constants.DebugLogTestTimestamp}] [{Constants.Constants.DebugLogTestThreadId}] [{level}] [{category}] {message}";

    public static async IAsyncEnumerable<string> ToAsyncEnumerable(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            yield return line;

            await Task.Yield();
        }
    }

    public static async IAsyncEnumerable<string> YieldThenThrow(IEnumerable<string> lines, Exception exception)
    {
        foreach (var line in lines)
        {
            yield return line;

            await Task.Yield();
        }

        throw exception;
    }
}
