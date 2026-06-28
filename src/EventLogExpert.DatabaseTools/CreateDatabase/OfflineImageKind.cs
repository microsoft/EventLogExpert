// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.DatabaseTools.CreateDatabase;

/// <summary>
///     How the offline Windows image named by <see cref="CreateDatabaseRequest.OfflineImagePath" /> is accessed when
///     building a provider database from it. Only <see cref="Directory" /> is supported today; <see cref="Wim" /> and
///     <see cref="Iso" /> are reserved for later phases that mount the image before reading.
/// </summary>
public enum OfflineImageKind
{
    Directory,
    Wim,
    Iso
}
