// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Common.Preferences;
using Fluxor;

namespace EventLogExpert.UI.FilterGroup;

public sealed class Effects(
    IState<FilterGroupState> filterGroupState,
    IPreferencesProvider preferencesProvider)
{
    [EffectMethod(typeof(AddGroupAction))]
    public Task HandleAddGroup(IDispatcher dispatcher)
    {
        preferencesProvider.SavedFiltersPreference = filterGroupState.Value.Groups;

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(ImportGroupsAction))]
    public Task HandleImportGroups(IDispatcher dispatcher)
    {
        preferencesProvider.SavedFiltersPreference = filterGroupState.Value.Groups;

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(LoadGroupsAction))]
    public Task HandleLoadGroups(IDispatcher dispatcher)
    {
        var loadedFilters = preferencesProvider.SavedFiltersPreference;

        dispatcher.Dispatch(new LoadGroupsSuccessAction(loadedFilters));

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(RemoveGroupAction))]
    public Task HandleRemoveGroup(IDispatcher dispatcher)
    {
        preferencesProvider.SavedFiltersPreference = filterGroupState.Value.Groups;

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(SetGroupAction))]
    public Task HandleSetGroup(IDispatcher dispatcher)
    {
        preferencesProvider.SavedFiltersPreference = filterGroupState.Value.Groups;

        return Task.CompletedTask;
    }
}
