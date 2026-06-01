// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.FilterPane;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed class Effects(IFilterLibraryStore store, IState<FilterLibraryState> state, ITraceLogger logger)
{
    [EffectMethod]
    public Task HandleAddLibraryEntry(AddLibraryEntryAction action, IDispatcher dispatcher)
    {
        try
        {
            store.Add(action.Entry);
            dispatcher.Dispatch(new AddLibraryEntrySuccessAction(action.Entry));
        }
        catch (Exception ex)
        {
            logger.Warning($"FilterLibrary Add failed for entry {action.Entry.Id}. {ex.Message}");
        }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleApplyLibraryEntry(ApplyLibraryEntryAction action, IDispatcher dispatcher)
    {
        LibraryEntry? entry = state.Value.Entries.FirstOrDefault(e => e.Id == action.EntryId);

        if (entry is null) { return Task.CompletedTask; }

        ImmutableList<SavedFilter> filters = ExtractFilters(entry);

        dispatcher.Dispatch(new MergeFiltersAction(filters));
        dispatcher.Dispatch(new RecordEntryAppliedAction(action.EntryId));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleDeleteLibraryEntry(DeleteLibraryEntryAction action, IDispatcher dispatcher)
    {
        try
        {
            store.Delete(action.EntryId);
            dispatcher.Dispatch(new DeleteLibraryEntrySuccessAction(action.EntryId));
        }
        catch (Exception ex)
        {
            logger.Warning($"FilterLibrary Delete failed for entry {action.EntryId}. {ex.Message}");
        }

        return Task.CompletedTask;
    }

    [EffectMethod(typeof(LoadLibraryAction))]
    public Task HandleLoadLibrary(IDispatcher dispatcher)
    {
        try
        {
            var entries = store.LoadAll().ToImmutableList();
            dispatcher.Dispatch(new LoadLibrarySuccessAction(entries));
        }
        catch (Exception ex)
        {
            logger.Warning($"FilterLibrary load failed; rendering error state. {ex.Message}");
            dispatcher.Dispatch(new LoadLibraryFailureAction());
        }

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleReplaceWithLibraryEntry(ReplaceWithLibraryEntryAction action, IDispatcher dispatcher)
    {
        LibraryEntry? entry = state.Value.Entries.FirstOrDefault(e => e.Id == action.EntryId);

        if (entry is null) { return Task.CompletedTask; }

        ImmutableList<SavedFilter> filters = ExtractFilters(entry);

        dispatcher.Dispatch(new ReplaceFiltersAction(filters));
        dispatcher.Dispatch(new RecordEntryAppliedAction(action.EntryId));

        return Task.CompletedTask;
    }

    [EffectMethod]
    public Task HandleUpdateLibraryEntry(UpdateLibraryEntryAction action, IDispatcher dispatcher)
    {
        try
        {
            store.Update(action.Entry);
            dispatcher.Dispatch(new UpdateLibraryEntrySuccessAction(action.Entry));
        }
        catch (Exception ex)
        {
            logger.Warning($"FilterLibrary Update failed for entry {action.Entry.Id}. {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private static ImmutableList<SavedFilter> ExtractFilters(LibraryEntry entry) =>
        entry switch
        {
            LibraryEntrySavedFilter f => [f.Filter],
            LibraryEntryPreset p => p.Filters,
            _ => throw new InvalidOperationException($"Unhandled LibraryEntry type '{entry.GetType().FullName}'."),
        };
}
