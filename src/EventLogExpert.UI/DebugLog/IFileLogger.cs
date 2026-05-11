// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.DebugLog;

public interface IFileLogger
{
    Task ClearAsync();

    IAsyncEnumerable<string> LoadAsync(CancellationToken cancellationToken = default);
}
