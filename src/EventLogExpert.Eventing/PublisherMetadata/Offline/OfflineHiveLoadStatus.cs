// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>
///     The outcome of attempting to load an offline image registry hive. Distinguishes the cases the caller must
///     surface differently: a clean load, a path that is not a hive at all, a dirty hive that needs recovery the current
///     (non-elevated) process cannot perform, and a recovery that was attempted but failed.
/// </summary>
internal enum OfflineHiveLoadStatus
{
    /// <summary>The hive loaded (either directly, or recovered under administrator privileges).</summary>
    Loaded,

    /// <summary>The file is missing or is not a registry hive (no <c>regf</c> signature); no recovery was attempted.</summary>
    NotAHive,

    /// <summary>The hive is dirty and needs registry recovery, which requires running as administrator.</summary>
    NeedsElevation,

    /// <summary>The hive needed recovery and the process is elevated, but the recovery load still failed.</summary>
    RecoveryFailed
}
