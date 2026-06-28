// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>The outcome of extracting an image from a WIM via <see cref="OfflineWimImage" />.</summary>
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
