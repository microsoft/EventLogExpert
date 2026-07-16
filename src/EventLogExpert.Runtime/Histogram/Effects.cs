// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Runtime.Histogram;

internal sealed class Effects(IHistogramPreferencesProvider preferences)
{
    private readonly IHistogramPreferencesProvider _preferences = preferences;

    [EffectMethod]
    public Task HandleSetHistogramVisible(SetHistogramVisibleAction action, IDispatcher dispatcher)
    {
        // Skip the redundant write during startup hydration, which dispatches the persisted value straight back.
        if (_preferences.HistogramVisiblePreference != action.IsVisible)
        {
            _preferences.HistogramVisiblePreference = action.IsVisible;
        }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleStoreInitialized(StoreInitializedAction action, IDispatcher dispatcher)
    {
        dispatcher.Dispatch(new SetHistogramVisibleAction(_preferences.HistogramVisiblePreference));

        return Task.CompletedTask;
    }
}
