// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

public enum WimImageListStatus
{
    Ok,

    NotAWim
}

public sealed record WimImageEntry(int Index, string Name, string Edition, long? TotalBytes);

public sealed record WimImageList(WimImageListStatus Status, IReadOnlyList<WimImageEntry> Images)
{
    internal static WimImageList NotAWim { get; } = new(WimImageListStatus.NotAWim, []);
}
