// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Services;

namespace EventLogExpert.UI.Update;

public interface IUpdateService
{
    Task CheckForUpdates(bool usePreRelease, bool userInitiated = false);

    Task<ReleaseNotesContent?> GetReleaseNotes();
}
