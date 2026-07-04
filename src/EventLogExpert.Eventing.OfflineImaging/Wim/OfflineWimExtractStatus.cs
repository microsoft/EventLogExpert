// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.OfflineImaging.Wim;

public enum OfflineWimExtractStatus
{
    Extracted,
    NotAWim,
    IndexOutOfRange,
    NeedsElevation,
    ApplyFailed,
    InsufficientSpace,
    Cancelled
}
