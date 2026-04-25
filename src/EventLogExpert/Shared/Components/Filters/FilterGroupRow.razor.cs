// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using EventLogExpert.UI;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Store.FilterGroup;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components.Filters;

public sealed partial class FilterGroupRow : EditableFilterRowBase
{
    [Parameter] public FilterGroupId ParentId { get; set; }

    protected override void DispatchRemoveFilter()
    {
        if (Value is not { } savedFilter) { return; }

        Dispatcher.Dispatch(new FilterGroupAction.RemoveFilter(ParentId, savedFilter.Id));
    }

    protected override void DispatchSetFilter(FilterModel filter) =>
        Dispatcher.Dispatch(new FilterGroupAction.SetFilter(ParentId, filter));

    protected override void DispatchToggleExclusion(FilterId id) =>
        Dispatcher.Dispatch(new FilterGroupAction.ToggleFilterExcluded(ParentId, id));

    protected override async ValueTask<FilterModel?> TrySaveAsync(FilterDraftModel draft)
    {
        var compiled = await base.TrySaveAsync(draft);

        if (compiled is null) { return null; }

        // FilterGroupRow only edits the raw expression text, so any structured BasicFilter hydrated from
        // the saved value is stale once the user edits the text. Force Advanced + null BasicFilter unless
        // the text is unchanged from the saved value.
        if (Value is { FilterType: FilterType.Basic } savedFilter &&
            string.Equals(savedFilter.ComparisonText, draft.ComparisonText, StringComparison.Ordinal))
        {
            return compiled;
        }

        return compiled with { FilterType = FilterType.Advanced, BasicFilter = null };
    }
}
