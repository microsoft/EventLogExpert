// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public readonly record struct FilterGroupId(Guid Value)
{
    public static FilterGroupId Create() => new(Guid.NewGuid());
}
