// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Services;

namespace EventLogExpert.Services;

public static class BuildServices
{
    public static IServiceCollection AddBuildServices(this IServiceCollection services)
    {
        services.AddSingleton<ICurrentVersionProvider, CurrentVersionProvider>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IGitHubService, GitHubService>();
        services.AddSingleton<IDeploymentService, DeploymentService>();

        return services;
    }
}
