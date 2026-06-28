// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.PublisherMetadata.Offline;

/// <summary>Whether a WIM's image-index metadata could be read.</summary>
public enum WimImageListStatus
{
    /// <summary>The metadata was read; <see cref="WimImageList.Images" /> lists every image.</summary>
    Ok,

    /// <summary>The path is not a readable WIM/ESD image (missing, corrupt, locked, or access-denied).</summary>
    NotAWim
}

/// <summary>One image inside a WIM, parsed from its <c>&lt;IMAGE&gt;</c> metadata.</summary>
/// <param name="Index">1-based image index passed to <c>--wim-index</c>.</param>
/// <param name="Name">Display name (e.g. "Windows Server 2019 Standard (Desktop Experience)").</param>
/// <param name="Edition">Edition id (e.g. "ServerStandard"), or empty when the metadata omits it.</param>
/// <param name="TotalBytes">Extracted size in bytes, or <see langword="null" /> when the metadata omits it.</param>
public sealed record WimImageEntry(int Index, string Name, string Edition, long? TotalBytes);

/// <summary>
///     The images contained in a WIM file (or <see cref="WimImageListStatus.NotAWim" /> when the file could not be
///     read). Produced by
///     <see cref="OfflineWimImage.ReadIndexList(string, EventLogExpert.Logging.Abstractions.ITraceLogger?)" /> so a caller
///     can list the available <c>--wim-index</c> choices.
/// </summary>
public sealed record WimImageList(WimImageListStatus Status, IReadOnlyList<WimImageEntry> Images)
{
    internal static WimImageList NotAWim { get; } = new(WimImageListStatus.NotAWim, []);
}
