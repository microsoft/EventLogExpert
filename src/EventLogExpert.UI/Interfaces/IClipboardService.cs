// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Interfaces;

public interface IClipboardService
{
    Task CopySelectedEvent(CopyType? copyType = null);

    Task CopyTextAsync(string text);
}
