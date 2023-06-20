// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Store.FilterCache;

public record FilterCacheAction
{
    public record AddRecentFilter(CachedFilterModel Filter);

    public record RemoveRecentFilter(CachedFilterModel Filter);

    public record ToggleFavoriteFilter(CachedFilterModel Filter);

    public record OpenMenu;
}
