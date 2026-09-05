// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterLenses;

namespace EventLogExpert.Runtime.Announcement;

public interface IAnnouncementService
{
    event Action? StateChanged;

    CurrentAnnouncement Current { get; }

    void Announce(string message);

    void AnnounceLensGroupSaved(string name);

    void AnnounceLensKept(FilterLensLabel label);

    void AnnounceLensesSavedAll();
}
