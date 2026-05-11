// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Filter;

public readonly record struct FilterId(Guid Value)
{
    public static FilterId Create() => new(Guid.NewGuid());
}
