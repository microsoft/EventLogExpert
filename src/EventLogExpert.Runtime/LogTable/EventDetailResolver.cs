// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using Fluxor;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class EventDetailResolver(IState<RawEventStoreState> rawEventStore) : IEventDetailResolver
{
    private readonly IState<RawEventStoreState> _rawEventStore = rawEventStore;

    public bool TryResolve(EventLocator locator, [NotNullWhen(true)] out ResolvedEvent? detail)
    {
        detail = null;

        if (!_rawEventStore.Value.ByLog.TryGetValue(locator.LogId, out var store)) { return false; }

        var reader = store.CreateReader(locator.LogId);

        if (locator.Generation != reader.Generation) { return false; }

        if (locator.Index < 0 || locator.Index >= reader.Count) { return false; }

        detail = reader.GetDetail(locator);

        return true;
    }
}
