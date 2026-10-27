// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Banner;

public interface INotificationBulkDismissService
{
    NotificationDismissSnapshot CaptureDismissSnapshot(bool includeAttention);

    int DismissSnapshot(NotificationDismissSnapshot snapshot);
}
