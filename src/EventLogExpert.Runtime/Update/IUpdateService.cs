// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Update.ReleaseNotes;

namespace EventLogExpert.Runtime.Update;

public interface IUpdateService
{
    Task CheckForUpdates(bool usePreRelease, bool userInitiated = false);

    Task<ReleaseNotesContent?> GetReleaseNotes();
}
