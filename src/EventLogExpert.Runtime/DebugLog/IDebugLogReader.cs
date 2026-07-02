// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.DebugLog;

/// <summary>
///     Read/clear side of the debug log, consumed by the in-app debug-log viewer. This is intentionally an
///     application concern (not part of the logging library): <see cref="LoadAsync" /> reads the written file back, and
///     <see cref="ClearAsync" /> truncates it.
/// </summary>
public interface IDebugLogReader
{
    Task ClearAsync();

    IAsyncEnumerable<string> LoadAsync(CancellationToken cancellationToken = default);
}
