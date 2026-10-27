// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Banner;

internal sealed record NotificationToast(
    NotificationSeverity Severity,
    string Title,
    string Message,
    int ArrivalCount);
