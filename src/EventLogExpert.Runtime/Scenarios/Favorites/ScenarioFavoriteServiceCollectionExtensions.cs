// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.Runtime.Scenarios.Favorites;

public static class ScenarioFavoriteServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddScenarioFavoriteSqliteStore(string dbPath)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);

            services.AddSingleton<IScenarioFavoriteStore>(_ => new ScenarioFavoriteSqliteStore(dbPath));

            return services;
        }
    }
}
