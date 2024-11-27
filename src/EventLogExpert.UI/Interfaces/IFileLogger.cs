// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Interfaces;

public interface IFileLogger
{
    Task ClearAsync();

    IAsyncEnumerable<string> LoadAsync();
}
