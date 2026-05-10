// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Services;

public interface IUpdateService
{
    Task CheckForUpdates(bool usePreRelease, bool userInitiated = false);

    Task<ReleaseNotesContent?> GetReleaseNotes();
}
