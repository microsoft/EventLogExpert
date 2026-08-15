// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Filtering.Compilation;

namespace EventLogExpert.Filtering.Evaluation;

/// <summary>
///     Computes per-log matches for a filter that references event XML without reopening the log with pre-rendered
///     XML: cheaper predicates narrow the candidate rows, an injected <see cref="IEventXmlBatchScanner" /> renders XML for
///     those candidates on demand, and the complete filter is evaluated per candidate via the row path.
/// </summary>
public sealed class XmlFilterMatcher : IXmlFilterMatcher
{
    private readonly IEventXmlBatchScanner _scanner;

    public XmlFilterMatcher() : this(new EventXmlBatchScanner()) { }

    internal XmlFilterMatcher(IEventXmlBatchScanner scanner)
    {
        ArgumentNullException.ThrowIfNull(scanner);

        _scanner = scanner;
    }

    public XmlFilterMatch ComputeMatch(
        IEventColumnReader reader,
        Filter filter,
        string owningLog,
        LogPathType pathType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentException.ThrowIfNullOrEmpty(owningLog);

        int count = reader.Count;
        bool[] matches = new bool[count];

        long[] recordIds = new long[count];
        bool[] hasRecordId = new bool[count];
        reader.CopyInt64Column(EventFieldId.RecordId, recordIds, hasRecordId);

        Func<IEventColumnReader, EventLocator, bool> isXmlCandidate = FilterService.CompileXmlCandidatePredicate(filter);

        // Phase 1 (cheap, columnar): a candidate is a date-surviving row with a record id whose cheap (non-XML) filter
        // conjuncts do not decisively fail - only those rows need their XML rendered. The date range is a global
        // necessary condition, so a date-excluded row is a non-match. A date-surviving row that every XML filter rules
        // out via a cheap conjunct is XML-independent: it evaluates the same with empty XML, so it joins the rows that
        // have no record id on the empty-XML path and is never rendered.
        Dictionary<long, int> candidateIndexByRecordId = new(count);
        List<int> emptyXmlRows = [];
        DateFilter? activeDateFilter = filter.DateFilter is { IsEnabled: true } enabledDateFilter ? enabledDateFilter : null;

        for (int index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!hasRecordId[index]) { emptyXmlRows.Add(index); continue; }

            EventLocator locator = reader.LocatorAt(index);

            if (activeDateFilter is not null && !DateSurvives(reader, locator, activeDateFilter)) { continue; }

            if (isXmlCandidate(reader, locator)) { candidateIndexByRecordId[recordIds[index]] = index; }
            else { emptyXmlRows.Add(index); }
        }

        // Phase 2 (on demand): the scanner renders XML only for candidate record ids; evaluate the complete filter with
        // the rendered XML spliced into the rehydrated row. Skipped entirely when nothing is a candidate - no log scan.
        bool[] evaluated = new bool[count];

        if (candidateIndexByRecordId.Count > 0)
        {
            foreach (ScannedEventXml scanned in
                _scanner.Scan(owningLog, pathType, candidateIndexByRecordId.ContainsKey, cancellationToken))
            {
                if (!candidateIndexByRecordId.TryGetValue(scanned.RecordId, out int index)) { continue; }

                evaluated[index] = true;
                matches[index] = Matches(reader, reader.LocatorAt(index), filter, scanned.Xml);
            }
        }

        // Phase 3: candidates the scan never yielded (record rolled out between snapshot and scan), rows without a
        // record id, and non-candidate rows are evaluated with empty XML - an unresolved or XML-independent row is
        // evaluated with empty XML, NOT an unconditional no-match. This runs even when the Phase 2 scan was skipped.
        foreach ((_, int index) in candidateIndexByRecordId)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (evaluated[index]) { continue; }

            matches[index] = Matches(reader, reader.LocatorAt(index), filter, string.Empty);
        }

        foreach (int index in emptyXmlRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            matches[index] = Matches(reader, reader.LocatorAt(index), filter, string.Empty);
        }

        return new XmlFilterMatch(reader.LogId, reader.Generation, reader.ContentVersion, count, matches);
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
