// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.EventProviderDatabase;

public static class ProviderDatabaseSchemaVersion
{
    public const int Current = 4;

    /// <summary>
    ///     Sentinel used when on-disk schema detection cannot identify any known
    ///     ProviderDetails shape. Distinguishes "unknown / possibly corrupt" from a
    ///     known-but-legacy version so callers can surface a more accurate error.
    /// </summary>
    public const int Unknown = 0;
}
