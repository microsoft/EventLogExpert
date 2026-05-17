// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Banner;

public readonly record struct BannerId(Guid Value)
{
    public static BannerId Create() => new(Guid.NewGuid());
}
