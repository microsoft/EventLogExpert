// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.StatusBar;

public readonly record struct StatusActivityId(Guid Value)
{
    public static StatusActivityId Create() => new(Guid.NewGuid());
}
