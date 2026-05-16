// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Persistence;

public readonly record struct FilterId(Guid Value)
{
    public static FilterId Create() => new(Guid.NewGuid());
}
