// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

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
