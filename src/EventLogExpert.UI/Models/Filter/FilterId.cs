// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public readonly record struct FilterId(Guid Value)
{
    public static FilterId Create() => new(Guid.NewGuid());
}
