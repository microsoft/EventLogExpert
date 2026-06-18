// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Scenarios.Favorites;

internal sealed record SetScenarioFavoriteAction(string ScenarioId, string ScenarioName, bool IsFavorite);
