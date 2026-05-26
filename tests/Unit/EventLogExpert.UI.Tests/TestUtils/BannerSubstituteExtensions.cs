// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Banner;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.TestUtils;

/// <summary>
///     Test helper that creates substitutes for all 5 banner facets and registers them in a single call. Intended for
///     tests (e.g. <c>BannerHostTests</c>) that exercise multiple facets together. Tests that only touch one or two facets
///     should use direct <c>Substitute.For&lt;IXyzBannerService&gt;()</c> to keep per-test facet usage visible at the call
///     site.
/// </summary>
internal static class BannerSubstituteExtensions
{
    public static void AddBannerSubstitutes(
        this IServiceCollection services,
        out IAttentionBannerService attention,
        out IProgressBannerService progress,
        out ICriticalErrorService critical,
        out IErrorBannerService error,
        out IInfoBannerService info)
    {
        attention = Substitute.For<IAttentionBannerService>();
        progress = Substitute.For<IProgressBannerService>();
        critical = Substitute.For<ICriticalErrorService>();
        error = Substitute.For<IErrorBannerService>();
        info = Substitute.For<IInfoBannerService>();

        services.AddSingleton(attention);
        services.AddSingleton(progress);
        services.AddSingleton(critical);
        services.AddSingleton(error);
        services.AddSingleton(info);
    }
}
