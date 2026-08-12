// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Sources;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Scenarios.Favorites;

public interface IScenarioFavoritesSource : IChangeNotifier
{
    ImmutableHashSet<string> FavoriteScenarioIds { get; }
}
