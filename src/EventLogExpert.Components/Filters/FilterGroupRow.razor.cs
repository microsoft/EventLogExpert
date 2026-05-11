// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Components.Filters.Base;
using EventLogExpert.UI.Filter;
using EventLogExpert.UI.FilterGroup;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Components.Filters;

public sealed partial class FilterGroupRow : EditableFilterRowBase
{
    [Parameter] public FilterGroupId ParentId { get; set; }

    protected override void DispatchRemoveFilter()
    {
        if (Value is not { } savedFilter) { return; }

        Dispatcher.Dispatch(new RemoveFilterAction(ParentId, savedFilter.Id));
    }

    protected override void DispatchSetFilter(SavedFilter filter) =>
        Dispatcher.Dispatch(new SetFilterAction(ParentId, filter));

    protected override void DispatchToggleExclusion(FilterId id) =>
        Dispatcher.Dispatch(new ToggleFilterExcludedAction(ParentId, id));

    protected override async ValueTask<SavedFilter?> TrySaveAsync(FilterDraft draft)
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
