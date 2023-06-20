// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Store.FilterCache;

public record FilterCacheAction
{
    public record AddRecentFilter(string ComparisonString);

    public record RemoveRecentFilter(string ComparisonString);

    public record AddFavoriteFilter(string ComparisonString);

    public record RemoveFavoriteFilter(string ComparisonString);

    public record OpenMenu;
}
