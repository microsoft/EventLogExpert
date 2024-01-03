// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Store.FilterColor;

public sealed record FilterColorAction
{
    public sealed record ClearAllFilters;

    public sealed record RemoveFilter(Guid Id);

    public sealed record SetFilter(FilterModel Filter);
}
