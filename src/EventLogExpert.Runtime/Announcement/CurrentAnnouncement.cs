// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Announcement;

public sealed record CurrentAnnouncement(Announcement Payload, int Sequence);
