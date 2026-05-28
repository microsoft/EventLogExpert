// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Banner;

public interface IProgressBannerService
{
    event Action StateChanged;

    BannerProgressEntry? BackgroundProgress { get; }

    BannerProgressEntry? ManageDatabasesProgress { get; }
}
