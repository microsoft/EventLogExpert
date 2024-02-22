// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public readonly record struct EventLogId(Guid Value)
{
    public static EventLogId Create() => new(Guid.NewGuid());
}
