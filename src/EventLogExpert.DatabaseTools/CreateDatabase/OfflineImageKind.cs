// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.DatabaseTools.CreateDatabase;

/// <summary>
///     How the offline Windows image named by <see cref="CreateDatabaseRequest.OfflineImagePath" /> is accessed when
///     building a provider database from it. <see cref="Directory" /> reads a mounted volume or extracted image folder;
///     <see cref="Wim" /> extracts an image (by <see cref="CreateDatabaseRequest.WimIndex" />) from a <c>.wim</c>/
///     <c>.esd</c> file first. <see cref="Iso" /> is reserved for a later phase.
/// </summary>
public enum OfflineImageKind
{
    Directory,
    Wim,
    Iso
}
