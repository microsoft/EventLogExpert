// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Scenarios.Favorites;

internal interface IScenarioFavoriteStore
{
    Task AddAsync(string scenarioId, CancellationToken cancellationToken = default);

    Task DeleteAsync(string scenarioId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> LoadAllAsync(CancellationToken cancellationToken = default);
}
