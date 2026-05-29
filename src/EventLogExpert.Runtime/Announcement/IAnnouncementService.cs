// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Announcement;

/// <summary>
///     Singleton service backing the application-level polite live region. Survives modal teardown so completion
///     announcements (e.g., "Settings saved", "Database imported") are still announced by screen readers after the
///     originating modal closes.
/// </summary>
public interface IAnnouncementService
{
    event Action? StateChanged;

    string CurrentAnnouncement { get; }

    void Announce(string message);
}
