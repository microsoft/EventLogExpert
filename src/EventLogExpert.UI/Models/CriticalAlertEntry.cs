// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public sealed record CriticalAlertEntry(Guid Id, string Title, string Message, DateTime CreatedUtc);
