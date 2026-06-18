// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Scenarios.Favorites;

internal sealed record LoadScenarioFavoritesSuccessAction(ImmutableHashSet<string> FavoriteScenarioIds);
