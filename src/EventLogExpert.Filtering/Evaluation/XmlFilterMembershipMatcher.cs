// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Resolvers;

namespace EventLogExpert.Filtering.Evaluation;

/// <summary>
///     Computes per-log membership for a filter that references event XML without reopening the log with pre-rendered
///     XML: cheaper predicates narrow the candidate rows, an injected <see cref="IEventXmlBatchScanner" /> renders XML for
///     those candidates on demand, and the complete filter is evaluated per candidate via the row path.
/// </summary>
internal sealed class XmlFilterMembershipMatcher
{
    private readonly IEventXmlBatchScanner _scanner;

    public XmlFilterMembershipMatcher() : this(new EventXmlBatchScanner()) { }

    internal XmlFilterMembershipMatcher(IEventXmlBatchScanner scanner)
    {
        ArgumentNullException.ThrowIfNull(scanner);

        _scanner = scanner;
    }

    public XmlFilterMembership ComputeMembership(
        IEventColumnReader reader,
        Filter filter,
        string owningLog,
        LogPathType pathType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrEmpty(owningLog);

        int count = reader.Count;
        bool[] membership = new bool[count];

        long[] recordIds = new long[count];
        bool[] hasRecordId = new bool[count];
        reader.CopyInt64Column(EventFieldId.RecordId, recordIds, hasRecordId);

        // Phase 1 (cheap, columnar): candidate = date-surviving rows with a record id. The date range is a necessary
        // condition for membership, so a date-excluded row is a non-member and never needs its XML rendered.
        Dictionary<long, int> candidateIndexByRecordId = new(count);
        List<int> unmappableRows = [];
        DateFilter? activeDateFilter = filter.DateFilter is { IsEnabled: true } enabledDateFilter ? enabledDateFilter : null;

        for (int index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!hasRecordId[index]) { unmappableRows.Add(index); continue; }

            if (activeDateFilter is not null && !DateSurvives(reader, reader.LocatorAt(index), activeDateFilter)) { continue; }

            candidateIndexByRecordId[recordIds[index]] = index;
        }

        // Phase 2 (on demand): the scanner renders XML only for candidate record ids; evaluate the complete filter with
        // the rendered XML spliced into the rehydrated row.
        bool[] evaluated = new bool[count];

        foreach (ScannedEventXml scanned in
            _scanner.Scan(owningLog, pathType, candidateIndexByRecordId.ContainsKey, cancellationToken))
        {
            if (!candidateIndexByRecordId.TryGetValue(scanned.RecordId, out int index)) { continue; }

            evaluated[index] = true;
            membership[index] = Matches(reader, reader.LocatorAt(index), filter, scanned.Xml);
        }

        // Phase 3 (defensive): candidates the scan never yielded (record rolled out between snapshot and scan) and rows
        // without a record id are evaluated with empty XML - an unresolved XML is empty, NOT an unconditional no-match.
        foreach ((_, int index) in candidateIndexByRecordId)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (evaluated[index]) { continue; }

            membership[index] = Matches(reader, reader.LocatorAt(index), filter, string.Empty);
        }

        foreach (int index in unmappableRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            membership[index] = Matches(reader, reader.LocatorAt(index), filter, string.Empty);
        }

        return new XmlFilterMembership(reader.LogId, reader.Generation, reader.ContentVersion, count, membership);
    }

    private static bool DateSurvives(IEventColumnReader reader, EventLocator locator, DateFilter dateFilter)
    {
        reader.GetField(locator, EventFieldId.TimeCreated).TryGetDateTime(out DateTime timeCreated);

        return timeCreated >= dateFilter.After && timeCreated <= dateFilter.Before;
    }

    private static bool Matches(IEventColumnReader reader, EventLocator locator, Filter filter, string xml)
    {
        ResolvedEvent resolvedEvent = reader.GetDetail(locator) with { Xml = xml };

        return resolvedEvent.MatchesFilters(filter.Filters) && resolvedEvent.MatchesDateFilter(filter.DateFilter);
    }
}
