// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Menu;

/// <summary>
///     A progress phase reported by <see cref="IMenuActionService.OpenFolderAsync" /> so a caller can surface a
///     cancellable busy indicator only while cancellation is meaningful.
/// </summary>
public enum FolderOpenPhase
{
    Scanning,
    Opening
}
