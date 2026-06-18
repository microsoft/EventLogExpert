// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Scenarios.Favorites;

public interface IScenarioFavoriteCommands
{
    void Load();

    void SetFavorite(string scenarioId, string scenarioName, bool isFavorite);
}
