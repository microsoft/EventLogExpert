// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.DebugLog;

public interface IDebugLogReader
{
    Task ClearAsync();

    IAsyncEnumerable<string> LoadAsync(CancellationToken cancellationToken = default);
}
