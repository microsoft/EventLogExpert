// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Common.Files;

public interface IFileSaveService
{
    Task<string?> SaveStreamingAsync(
        string suggestedFileName,
        IReadOnlyDictionary<string, IReadOnlyList<string>> fileTypes,
        Func<Stream, CancellationToken, Task> writeContent,
        CancellationToken cancellationToken);
}
