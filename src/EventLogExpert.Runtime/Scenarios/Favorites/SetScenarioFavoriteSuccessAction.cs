// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Scenarios.Favorites;

internal sealed record SetScenarioFavoriteSuccessAction(string ScenarioId, string ScenarioName, bool IsFavorite);
