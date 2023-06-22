// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Store.FilterCache;

public record FilterCacheAction
{
    public record AddRecentFilter(FilterCacheModel Filter);

    public record RemoveRecentFilter(FilterCacheModel Filter);

    public record ToggleFavoriteFilter(FilterCacheModel Filter);

    public record OpenMenu;
}
