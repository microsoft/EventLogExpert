// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Banner;

public enum ExportBlockReason
{
    Faulted,
    Updating,
    NoEvents,
    NoColumns,
    AlreadyInProgress
}

public sealed record ExportBlocked(ExportBlockReason Reason) : BannerMessage;

public sealed record ExportCanceled : BannerMessage;

public sealed record ExportFailed(string Detail) : BannerMessage;

public sealed record ExportComplete(int Count, string Path) : BannerMessage;
