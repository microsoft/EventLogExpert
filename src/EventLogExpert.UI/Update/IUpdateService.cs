// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Update.ReleaseNotes;

namespace EventLogExpert.UI.Update;

public interface IUpdateService
{
    Task CheckForUpdates(bool usePreRelease, bool userInitiated = false);

    Task<ReleaseNotesContent?> GetReleaseNotes();
}
