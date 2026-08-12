// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Common.Clipboard;

public interface IEventCopyFormatter
{
    Task<string> FormatAsync(EventCopyRequest request, CancellationToken cancellationToken = default);
}
