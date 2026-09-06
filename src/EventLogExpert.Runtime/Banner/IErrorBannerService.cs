// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Banner;

public interface IErrorBannerService
{
    event Action StateChanged;

    IReadOnlyList<ErrorBannerEntry> ErrorBanners { get; }

    void DismissError(BannerId id);

    BannerId ReportError(BannerMessage content, Func<Task>? action = null);
}
