// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Interfaces;

public interface IFileLogger
{
    event Action? DebugLogLoaded;

    Task ClearAsync();

    IAsyncEnumerable<string> LoadAsync();

    void LoadDebugLog();
}
