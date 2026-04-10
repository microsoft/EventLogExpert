// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace EventLogExpert.UI.Interfaces;

public interface IFileLogger
{
    event Action? DebugLogLoaded;

    Task ClearAsync();

    IAsyncEnumerable<string> LoadAsync();

    void LoadDebugLog();

    void Trace(string message, LogLevel level = LogLevel.Information);
}
