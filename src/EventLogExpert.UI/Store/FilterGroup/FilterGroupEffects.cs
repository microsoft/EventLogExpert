// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using Fluxor;

namespace EventLogExpert.UI.Store.FilterGroup;

public sealed class FilterGroupEffects(
    IState<FilterGroupState> filterGroupState,
    IPreferencesProvider preferencesProvider)
{
    [EffectMethod(typeof(FilterGroupAction.AddFilter))]
    public Task HandleAddFilter(IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new FilterGroupAction.UpdateDisplayGroups(filterGroupState.Value.Groups));

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(FilterGroupAction.AddGroup))]
    public Task HandleAddGroup(IDispatcher dispatcher)
    {
        preferencesProvider.SavedFiltersPreference = filterGroupState.Value.Groups;

        dispatcher.Dispatch(new FilterGroupAction.UpdateDisplayGroups(filterGroupState.Value.Groups));

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(FilterGroupAction.ImportGroups))]
    public Task HandleImportGroups(IDispatcher dispatcher)
    {
        preferencesProvider.SavedFiltersPreference = filterGroupState.Value.Groups;

        dispatcher.Dispatch(new FilterGroupAction.UpdateDisplayGroups(filterGroupState.Value.Groups));

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(FilterGroupAction.LoadGroups))]
    public Task HandleLoadGroups(IDispatcher dispatcher)
    {
        var loadedFilters = preferencesProvider.SavedFiltersPreference;

        dispatcher.Dispatch(new FilterGroupAction.LoadGroupsSuccess(loadedFilters));

        dispatcher.Dispatch(new FilterGroupAction.UpdateDisplayGroups(loadedFilters));

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(FilterGroupAction.RemoveFilter))]
    public Task HandleRemoveFilter(IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new FilterGroupAction.UpdateDisplayGroups(filterGroupState.Value.Groups));

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(FilterGroupAction.RemoveGroup))]
    public Task HandleRemoveGroup(IDispatcher dispatcher)
    {
        preferencesProvider.SavedFiltersPreference = filterGroupState.Value.Groups;

        dispatcher.Dispatch(new FilterGroupAction.UpdateDisplayGroups(filterGroupState.Value.Groups));

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(FilterGroupAction.SetFilter))]
    public Task HandleSetFilter(IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new FilterGroupAction.UpdateDisplayGroups(filterGroupState.Value.Groups));

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(FilterGroupAction.SetGroup))]
    public Task HandleSetGroup(IDispatcher dispatcher)
    {
        preferencesProvider.SavedFiltersPreference = filterGroupState.Value.Groups;

        dispatcher.Dispatch(new FilterGroupAction.UpdateDisplayGroups(filterGroupState.Value.Groups));

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(FilterGroupAction.ToggleFilter))]
    public Task HandleToggleFilter(IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new FilterGroupAction.UpdateDisplayGroups(filterGroupState.Value.Groups));

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(FilterGroupAction.ToggleGroup))]
    public Task HandleToggleGroup(IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new FilterGroupAction.UpdateDisplayGroups(filterGroupState.Value.Groups));

        return Task.CompletedTask;
    }
}
