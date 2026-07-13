// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.FilterLenses;

public readonly record struct FilterLensId(Guid Value)
{
    public static FilterLensId Create() => new(Guid.NewGuid());
}
