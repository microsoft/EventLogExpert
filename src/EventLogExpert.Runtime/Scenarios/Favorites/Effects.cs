// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Announcement;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Scenarios.Favorites;

internal sealed class Effects(
    IScenarioFavoriteStore store,
    IState<ScenarioFavoritesState> state,
    IAnnouncementService announcementService,
    ITraceLogger logger)
{
    private readonly SemaphoreSlim _writeGate = new(initialCount: 1, maxCount: 1);

    [EffectMethod(typeof(LoadScenarioFavoritesAction))]
    public async Task HandleLoadScenarioFavorites(IDispatcher dispatcher)
    {
        if (state.Value.IsLoaded) { return; }

        await _writeGate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (state.Value.IsLoaded) { return; }

            var ids = await store.LoadAllAsync().ConfigureAwait(false);
            dispatcher.Dispatch(new LoadScenarioFavoritesSuccessAction(ids.ToImmutableHashSet(StringComparer.Ordinal)));
        }
        catch (Exception ex)
        {
            logger.Warning($"ScenarioFavorites load failed. {ex.Message}");
        }
        finally
        {
            _writeGate.Release();
        }
    }

    [EffectMethod]
    public async Task HandleSetScenarioFavorite(SetScenarioFavoriteAction action, IDispatcher dispatcher)
    {
        if (state.Value.FavoriteScenarioIds.Contains(action.ScenarioId) == action.IsFavorite) { return; }

        await _writeGate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (state.Value.FavoriteScenarioIds.Contains(action.ScenarioId) == action.IsFavorite) { return; }

            if (action.IsFavorite)
            {
                await store.AddAsync(action.ScenarioId).ConfigureAwait(false);
            }
            else
            {
                await store.DeleteAsync(action.ScenarioId).ConfigureAwait(false);
            }

            announcementService.Announce(action.IsFavorite
                ? $"Added {action.ScenarioName} to favorites"
                : $"Removed {action.ScenarioName} from favorites");

            dispatcher.Dispatch(new SetScenarioFavoriteSuccessAction(action.ScenarioId, action.ScenarioName, action.IsFavorite));
        }
        catch (Exception ex)
        {
            logger.Warning($"ScenarioFavorites set failed for {action.ScenarioId}. {ex.Message}");
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
