// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.UI.Banner;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.TestUtils;

internal static class BannerHostDependenciesExtensions
{
    public static void AddBannerHostDependencies(this IServiceCollection services)
    {
        var attention = Substitute.For<IAttentionBannerService>();
        attention.AttentionEntries.Returns([]);
        attention.AttentionDismissed.Returns(false);
        services.AddSingleton(attention);

        var critical = Substitute.For<ICriticalErrorService>();
        critical.CurrentCritical.Returns((Exception?)null);
        services.AddSingleton(critical);

        var errors = Substitute.For<IErrorBannerService>();
        errors.ErrorBanners.Returns([]);
        services.AddSingleton(errors);

        var infos = Substitute.For<IInfoBannerService>();
        infos.InfoBanners.Returns([]);
        services.AddSingleton(infos);

        var progress = Substitute.For<IProgressBannerService>();
        progress.BackgroundProgress.Returns((BannerProgressEntry?)null);
        services.AddSingleton(progress);

        var modalCoordinator = Substitute.For<IModalCoordinator>();
        modalCoordinator.ActiveSession.Returns((ModalSession?)null);
        services.AddSingleton(modalCoordinator);

        services.AddSingleton<IBannerCycleStateService, BannerCycleStateService>();
    }
}
