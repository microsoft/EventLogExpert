// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Compilation;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Runtime.EventLog;

namespace EventLogExpert.Runtime.LogTable;

/// <summary>
///     Decides, per open log, whether the ordered view / presence must defer (XML not yet ready) and which survivor
///     predicate branch each row takes: the on-demand match bitset (a File log whose XML is scanned on demand) or the
///     columnar predicate (a log already loaded with a materialized XML column).
/// </summary>
internal static class XmlFilterGate
{
    public static Func<IEventColumnReader, EventLocator, bool> BuildSurvivorPredicate(
        Filter filter,
        EventLogConcurrencyState concurrencyState,
        XmlFilterMatchCache matchCache)
    {
        Func<IEventColumnReader, EventLocator, bool> columnar = FilterService.CompileSurvivorPredicate(filter);

        if (!filter.RequiresXml) { return columnar; }

        return (reader, locator) => concurrencyState.IsLoadedWithXml(locator.LogId) ?
            columnar(reader, locator) :
            matchCache.GetMatch(filter, locator.LogId)?.IsMatch(locator) ?? false;
    }

    public static bool IsDeferred(
        Filter filter,
        EventLogState eventLogState,
        RawEventStoreState rawEventStore,
        EventLogConcurrencyState concurrencyState,
        XmlFilterMatchCache matchCache)
    {
        if (!filter.RequiresXml || eventLogState.OpenLogs.IsEmpty) { return false; }

        foreach (OpenLogInfo log in eventLogState.OpenLogs.Values)
        {
            if (!IsXmlReady(log, filter, rawEventStore, concurrencyState, matchCache)) { return true; }
        }

        return false;
    }

    private static bool IsXmlReady(
        OpenLogInfo log,
        Filter filter,
        RawEventStoreState rawEventStore,
        EventLogConcurrencyState concurrencyState,
        XmlFilterMatchCache matchCache)
    {
        // A log loaded with a materialized XML column (a reloaded Channel, a File loaded with XML, or a newcomer opened
        // under the active filter) is evaluated by the columnar predicate.
        if (concurrencyState.IsLoadedWithXml(log.Id)) { return true; }

        // A live (Channel) log not loaded with XML still needs the reload escape hatch.
        if (log.Type != LogPathType.File) { return false; }

        // An at-rest File log is ready once its on-demand match matches the current store snapshot.
        return rawEventStore.ByLog.TryGetValue(log.Id, out EventColumnStore? store) &&
            matchCache.GetMatch(filter, log.Id) is { } match &&
            match.Generation == store.Generation &&
            match.ContentVersion == store.ContentVersion &&
            match.Count == store.Count;
    }
}
