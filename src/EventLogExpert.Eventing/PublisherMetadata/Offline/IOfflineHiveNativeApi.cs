// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>
///     The native registry operations <see cref="OfflineRegistryHive" /> orchestrates, behind an interface so the
///     load/recover/open/unload state machine can be unit-tested with a fake that returns crafted error codes - the real
///     dirty-hive recovery path cannot be exercised by the synthetic clean hives the other tests use (clean hives never
///     return the recovery-needed error), and it requires administrator privileges.
/// </summary>
internal interface IOfflineHiveNativeApi
{
    /// <summary>
    ///     Enumerates the immediate subkey names under <c>HKLM</c>, used by the orphan sweep to find <c>ELX_</c> recovery
    ///     mounts left by a crashed prior run. Behind the seam so the sweep is hermetic under a fake (no real HKLM read).
    /// </summary>
    IReadOnlyList<string> EnumerateHklmSubKeyNames();

    /// <summary>
    ///     Loads a hive as a private application subtree (<c>RegLoadAppKey</c>, no privilege required). Returns the Win32
    ///     error code; on success <paramref name="root" /> owns the hive (closing it unloads the hive).
    /// </summary>
    int LoadApplicationHive(string hiveFilePath, out SafeRegistryHandle? root);

    /// <summary>
    ///     Mounts a hive under <c>HKLM\</c><paramref name="mountSubKey" /> with log recovery (<c>RegLoadKey</c>). The
    ///     caller MUST hold a <see cref="TryEnterRecoveryPrivilege" /> scope. Returns the Win32 error code.
    /// </summary>
    int LoadHiveForRecovery(string mountSubKey, string hiveFilePath);

    /// <summary>Opens the root of a hive mounted at <c>HKLM\</c><paramref name="mountSubKey" /> for reading.</summary>
    RegistryKey? OpenMountedRoot(string mountSubKey);

    /// <summary>
    ///     Enters the privileged section enabling backup/restore for a recovery load or unload, or returns
    ///     <see langword="null" /> when the process is not elevated (the token lacks the privileges). The returned scope
    ///     reverts the privileges and leaves the section when disposed.
    /// </summary>
    IDisposable? TryEnterRecoveryPrivilege(ITraceLogger? logger);

    /// <summary>
    ///     Unmounts the hive at <c>HKLM\</c><paramref name="mountSubKey" /> (<c>RegUnLoadKey</c>). The caller MUST hold a
    ///     <see cref="TryEnterRecoveryPrivilege" /> scope. Returns the Win32 error code.
    /// </summary>
    int UnloadHive(string mountSubKey);
}
