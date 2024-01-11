// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using Fluxor;

namespace EventLogExpert.UI.Store.FilterGroup;

public sealed class FilterGroupEffects(
    IState<FilterGroupState> filterGroupState,
    IPreferencesProvider preferencesProvider)
{
    [EffectMethod(typeof(FilterGroupAction.AddGroup))]
    public Task HandleAddGroup(IDispatcher dispatcher)
    {
        preferencesProvider.SavedFiltersPreference = filterGroupState.Value.Groups;

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(FilterGroupAction.LoadGroups))]
    public Task HandleLoadGroups(IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new FilterGroupAction.LoadGroupsSuccess(preferencesProvider.SavedFiltersPreference));

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(FilterGroupAction.RemoveGroup))]
    public Task HandleRemoveGroup(IDispatcher dispatcher)
    {
        preferencesProvider.SavedFiltersPreference = filterGroupState.Value.Groups;

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(FilterGroupAction.SetGroup))]
    public Task HandleSetGroup(IDispatcher dispatcher)
    {
        preferencesProvider.SavedFiltersPreference = filterGroupState.Value.Groups;

        return Task.CompletedTask;
    }
}
