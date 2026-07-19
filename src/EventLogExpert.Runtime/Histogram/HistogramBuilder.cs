// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Runtime.Common.Display;
using EventLogExpert.Runtime.LogTable;
using System.Globalization;

namespace EventLogExpert.Runtime.Histogram;

public static class HistogramBuilder
{
    private const string ErrorCodeEventNoun = "error-code events";
    // The ErrorCode dimension keys on the lowercase win:HexInt32 / win:UnicodeString errorCode field, scoped to the update
    // and servicing providers so a generic errorCode field on an unrelated provider does not pollute the failure view.
    private const string ErrorCodeFieldName = "errorCode";

    private static readonly string[] s_parentProcessImageFields = ["ParentProcessName", "ParentImage"];
    private static readonly char[] s_pathSeparators = ['\\', '/'];
    private static readonly string[] s_processImageFields = ["NewProcessName", "Image"];

    // Microsoft-Windows-Servicing stores its failure HRESULT in a UserData Cbs*ChangeState/ErrorCode leaf (a hex string)
    // rather than an EventData errorCode field, so the ErrorCode dimension also probes these curated storage-key paths for
    // an eligible row whose EventData lacks the code. CbsPackageInitiateChanges carries no ErrorCode and is not listed.
    private static readonly string[] s_updateErrorCodeUserDataPaths = ["CbsPackageChangeState/ErrorCode", "CbsUpdateChangeState/ErrorCode"];

    private static readonly string[] s_updateProviders = ["Microsoft-Windows-WindowsUpdateClient", "Microsoft-Windows-Servicing"];

    public static HistogramData? Build(
        IEventColumnView view,
        HistogramDimension dimension,
        int maxBuckets,
        CancellationToken cancellationToken) =>
        Build(view, dimension, maxBuckets, useHighlightTie: false, highlightWinners: null, cancellationToken);

    public static HistogramData? BuildWithHighlightTie(
        IEventColumnView view,
        HistogramDimension dimension,
        int maxBuckets,
        byte[] highlightWinners,
        CancellationToken cancellationToken) =>
        Build(view, dimension, maxBuckets, useHighlightTie: true, highlightWinners, cancellationToken);

    private static HistogramData? Build(
        IEventColumnView view,
        HistogramDimension dimension,
        int maxBuckets,
        bool useHighlightTie,
        byte[]? highlightWinners,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBuckets, 1);
        cancellationToken.ThrowIfCancellationRequested();

        if (!view.TryGetTimeTicksRange(out long minTicks, out long maxTicks, cancellationToken)) { return null; }

        long spanTicks = maxTicks - minTicks + 1;
        long bucketSpanTicks = Math.Max(1, (spanTicks + maxBuckets - 1) / maxBuckets);
        int bucketCount = (int)Math.Min((spanTicks + bucketSpanTicks - 1) / bucketSpanTicks, maxBuckets);

        if (dimension is HistogramDimension.LogonType or HistogramDimension.TicketEncryptionType)
        {
            return BuildByEventData(view,
                ToEventDataFieldName(dimension),
                minTicks,
                maxTicks,
                bucketSpanTicks,
                bucketCount,
                useHighlightTie,
                highlightWinners,
                cancellationToken);
        }

        if (dimension is HistogramDimension.ErrorCode)
        {
            return BuildByErrorCode(view, minTicks, maxTicks, bucketSpanTicks, bucketCount, useHighlightTie, highlightWinners, cancellationToken);
        }

        if (dimension is HistogramDimension.ProcessImage or HistogramDimension.ParentProcessImage)
        {
            return BuildByEventDataString(view,
                ProcessImageFieldNames(dimension),
                minTicks,
                maxTicks,
                bucketSpanTicks,
                bucketCount,
                useHighlightTie,
                highlightWinners,
                cancellationToken);
        }

        (int[] slotCounts, int slotCount, IReadOnlyList<HistogramGroup> groups, uint[]? groupHighlightMasks) = dimension switch
        {
            HistogramDimension.Severity => ScanSeverity(view, minTicks, bucketSpanTicks, bucketCount, useHighlightTie, highlightWinners, cancellationToken),
            HistogramDimension.EventId => ScanByEventId(view, minTicks, bucketSpanTicks, bucketCount, useHighlightTie, highlightWinners, cancellationToken),
            HistogramDimension.Log => ScanByField(view,
                EventFieldId.OwningLog,
                OwningLogDisplay.DistinctShortNames,
                minTicks,
                bucketSpanTicks,
                bucketCount,
                useHighlightTie,
                highlightWinners,
                cancellationToken),
            _ => ScanByField(view, ToFieldId(dimension), static keys => keys, minTicks, bucketSpanTicks, bucketCount, useHighlightTie, highlightWinners, cancellationToken)
        };

        int total = 0;

        foreach (int count in slotCounts) { total += count; }

        return new HistogramData(
            slotCounts,
            slotCount,
            bucketCount,
            new DateTime(minTicks, DateTimeKind.Utc),
            new DateTime(maxTicks, DateTimeKind.Utc),
            total,
            bucketSpanTicks,
            groups,
            groupHighlightMasks);
    }

    private static HistogramData BuildByErrorCode(
        IEventColumnView view,
        long minTicks,
        long maxTicks,
        long bucketSpanTicks,
        int bucketCount,
        bool useHighlightTie,
        byte[]? highlightWinners,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<long, int>();
        view.CountEventDataHResults(ErrorCodeFieldName, s_updateProviders, s_updateErrorCodeUserDataPaths, counts, cancellationToken);

        var minUtc = new DateTime(minTicks, DateTimeKind.Utc);
        var maxUtc = new DateTime(maxTicks, DateTimeKind.Utc);

        if (counts.Count == 0)
        {
            // Total = 0 is the honest failure-subset count (not view.Count), and the noun keeps the always-present region
            // announcement accurate ("0 error-code events") over a view that may still hold successful (errorCode = 0) rows.
            return new HistogramData([], 0, bucketCount, minUtc, maxUtc, 0, bucketSpanTicks, [])
            {
                GroupingFieldAbsent = true,
                EventNoun = ErrorCodeEventNoun
            };
        }

        long[] targetCodes = counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Take(HistogramConstants.MaxGroupByCategories)
            .Select(pair => pair.Key)
            .ToArray();

        int slotCount = targetCodes.Length + 1;
        int[] slotCounts = new int[bucketCount * slotCount];
        uint[]? slotColorMask = useHighlightTie ? new uint[slotCount] : null;
        if (slotColorMask is null)
        {
            view.BucketTimeTicksByEventDataHResult(minTicks,
                bucketSpanTicks,
                bucketCount,
                ErrorCodeFieldName,
                s_updateProviders,
                s_updateErrorCodeUserDataPaths,
                targetCodes,
                slotCounts,
                cancellationToken);
        }
        else
        {
            view.BucketTimeTicksByEventDataHResultWithTie(RequireHighlightWinners(highlightWinners),
                slotColorMask,
                minTicks,
                bucketSpanTicks,
                bucketCount,
                ErrorCodeFieldName,
                s_updateProviders,
                s_updateErrorCodeUserDataPaths,
                targetCodes,
                slotCounts,
                cancellationToken);
        }

        int total = 0;

        foreach (int count in slotCounts) { total += count; }

        // Key = the raw invariant code (stable toggle key); Label = the 8-digit hex form plus its curated HRESULT symbol.
        string[] keys = Array.ConvertAll(targetCodes, code => code.ToString(CultureInfo.InvariantCulture));
        string[] labels = Array.ConvertAll(targetCodes, FormatErrorCodeLabel);
        IReadOnlyList<HistogramGroup> groups = HistogramGroups.ForCategories(keys, labels);

        return new HistogramData(
            slotCounts,
            slotCount,
            bucketCount,
            minUtc,
            maxUtc,
            total,
            bucketSpanTicks,
            groups,
            FoldGroupMasks(slotColorMask, groups))
        {
            EventNoun = ErrorCodeEventNoun
        };
    }

    private static HistogramData BuildByEventData(
        IEventColumnView view,
        string fieldName,
        long minTicks,
        long maxTicks,
        long bucketSpanTicks,
        int bucketCount,
        bool useHighlightTie,
        byte[]? highlightWinners,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<long, int>();
        view.CountEventDataValues(fieldName, counts, cancellationToken);

        var minUtc = new DateTime(minTicks, DateTimeKind.Utc);
        var maxUtc = new DateTime(maxTicks, DateTimeKind.Utc);

        if (counts.Count == 0)
        {
            // No row in the view yields a decodable whole-number value for this field - the field is absent from every row,
            // or every occurrence is non-numeric or out of range. Report the true survivor count (view.Count - every survivor
            // falls within the view's own min/max span) so the accessible region label isn't "0 events" over a non-empty span,
            // and flag the empty-state so the pane shows a message rather than a lone "Other" band.
            return new HistogramData([], 0, bucketCount, minUtc, maxUtc, view.Count, bucketSpanTicks, []) { GroupingFieldAbsent = true };
        }

        long[] targetCodes = counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Take(HistogramConstants.MaxGroupByCategories)
            .Select(pair => pair.Key)
            .ToArray();

        int slotCount = targetCodes.Length + 1;
        int[] slotCounts = new int[bucketCount * slotCount];
        uint[]? slotColorMask = useHighlightTie ? new uint[slotCount] : null;

        if (slotColorMask is null)
        {
            view.BucketTimeTicksByEventData(minTicks, bucketSpanTicks, bucketCount, fieldName, targetCodes, slotCounts, cancellationToken);
        }
        else
        {
            view.BucketTimeTicksByEventDataWithTie(RequireHighlightWinners(highlightWinners),
                slotColorMask,
                minTicks,
                bucketSpanTicks,
                bucketCount,
                fieldName,
                targetCodes,
                slotCounts,
                cancellationToken);
        }

        int total = 0;

        foreach (int count in slotCounts) { total += count; }

        // Key = the raw invariant code (stable toggle key); Label = the friendly decode, or the raw code when unrecognized.
        string[] keys = Array.ConvertAll(targetCodes, code => code.ToString(CultureInfo.InvariantCulture));
        string[] labels = Array.ConvertAll(
            targetCodes,
            code => EventDataValueDecoder.TryDecodeLabel(fieldName, code) ?? code.ToString(CultureInfo.InvariantCulture));

        IReadOnlyList<HistogramGroup> groups = HistogramGroups.ForCategories(keys, labels);

        return new HistogramData(
            slotCounts,
            slotCount,
            bucketCount,
            minUtc,
            maxUtc,
            total,
            bucketSpanTicks,
            groups,
            FoldGroupMasks(slotColorMask, groups));
    }

    private static HistogramData BuildByEventDataString(
        IEventColumnView view,
        string[] candidateFields,
        long minTicks,
        long maxTicks,
        long bucketSpanTicks,
        int bucketCount,
        bool useHighlightTie,
        byte[]? highlightWinners,
        CancellationToken cancellationToken)
    {
        var rawCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        view.CountEventDataStringValues(candidateFields, rawCounts, cancellationToken);

        var minUtc = new DateTime(minTicks, DateTimeKind.Utc);
        var maxUtc = new DateTime(maxTicks, DateTimeKind.Utc);

        var byShortName = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((string raw, int count) in rawCounts)
        {
            string? shortName = NormalizeProcessImage(raw);
            if (shortName is null) { continue; }

            byShortName[shortName] = byShortName.TryGetValue(shortName, out int existing) ? existing + count : count;
        }

        if (byShortName.Count == 0)
        {
            return new HistogramData([], 0, bucketCount, minUtc, maxUtc, view.Count, bucketSpanTicks, []) { GroupingFieldAbsent = true };
        }

        string[] topShortNames = byShortName
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(HistogramConstants.MaxGroupByCategories)
            .Select(pair => pair.Key)
            .ToArray();

        int slotCount = topShortNames.Length + 1;
        int otherSlot = topShortNames.Length;

        var shortNameToSlot = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < topShortNames.Length; index++) { shortNameToSlot[topShortNames[index]] = index; }

        var rawValueToSlot = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string raw in rawCounts.Keys)
        {
            string? shortName = NormalizeProcessImage(raw);
            rawValueToSlot[raw] = shortName is not null && shortNameToSlot.TryGetValue(shortName, out int slot) ? slot : otherSlot;
        }

        int[] slotCounts = new int[bucketCount * slotCount];
        uint[]? slotColorMask = useHighlightTie ? new uint[slotCount] : null;

        if (slotColorMask is null)
        {
            view.BucketTimeTicksByEventDataString(minTicks,
                bucketSpanTicks,
                bucketCount,
                candidateFields,
                rawValueToSlot,
                slotCount,
                slotCounts,
                cancellationToken);
        }
        else
        {
            view.BucketTimeTicksByEventDataStringWithTie(RequireHighlightWinners(highlightWinners),
                slotColorMask,
                minTicks,
                bucketSpanTicks,
                bucketCount,
                candidateFields,
                rawValueToSlot,
                slotCount,
                slotCounts,
                cancellationToken);
        }

        int total = 0;
        foreach (int count in slotCounts) { total += count; }

        IReadOnlyList<HistogramGroup> groups = HistogramGroups.ForCategories(topShortNames, topShortNames);

        return new HistogramData(slotCounts, slotCount, bucketCount, minUtc, maxUtc, total, bucketSpanTicks, groups, FoldGroupMasks(slotColorMask, groups));
    }

    private static uint[]? FoldGroupMasks(uint[]? slotColorMask, IReadOnlyList<HistogramGroup> groups)
    {
        if (slotColorMask is null) { return null; }

        uint[] groupMasks = new uint[groups.Count];

        for (int group = 0; group < groups.Count; group++)
        {
            uint mask = 0;

            foreach (int slot in groups[group].SlotIndices) { mask |= slotColorMask[slot]; }

            groupMasks[group] = mask;
        }

        return groupMasks;
    }

    private static string FormatErrorCodeLabel(long code)
    {
        string hex = "0x" + ((uint)code).ToString("X8", CultureInfo.InvariantCulture);
        string? symbol = EventDataValueDecoder.TryDecodeLabel(ErrorCodeFieldName, code);

        return symbol is null ? hex : $"{hex} {symbol}";
    }

    private static string? NormalizeProcessImage(string raw)
    {
        string trimmed = raw.Trim();

        if (trimmed is ['"', _, ..] && trimmed[^1] == '"') { trimmed = trimmed[1..^1].Trim(); }

        int slash = trimmed.LastIndexOfAny(s_pathSeparators);
        string tail = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;

        if (tail.Length == 0 || tail == "-") { return null; }

        return tail.ToLowerInvariant();
    }

    private static string[] ProcessImageFieldNames(HistogramDimension dimension) => dimension switch
    {
        HistogramDimension.ProcessImage => s_processImageFields,
        HistogramDimension.ParentProcessImage => s_parentProcessImageFields,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "Dimension is not a process image field.")
    };

    private static byte[] RequireHighlightWinners(byte[]? highlightWinners) =>
        highlightWinners ?? throw new InvalidOperationException("Highlight winners must be captured before tie bucketing.");

    private static (int[] SlotCounts, int SlotCount, IReadOnlyList<HistogramGroup> Groups, uint[]? GroupHighlightMasks) ScanByEventId(
        IEventColumnView view,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        bool useHighlightTie,
        byte[]? highlightWinners,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<int, int>();
        view.CountEventIds(counts, cancellationToken);

        int[] targetIds = counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Take(HistogramConstants.MaxGroupByCategories)
            .Select(pair => pair.Key)
            .ToArray();

        int slotCount = targetIds.Length + 1;
        int[] slotCounts = new int[bucketCount * slotCount];
        uint[]? slotColorMask = useHighlightTie ? new uint[slotCount] : null;

        if (slotColorMask is null)
        {
            view.BucketTimeTicksByEventId(minTicks,
                bucketSpanTicks,
                bucketCount,
                targetIds,
                slotCounts,
                cancellationToken);
        }
        else
        {
            view.BucketTimeTicksByEventIdWithTie(RequireHighlightWinners(highlightWinners),
                slotColorMask,
                minTicks,
                bucketSpanTicks,
                bucketCount,
                targetIds,
                slotCounts,
                cancellationToken);
        }

        string[] keys = Array.ConvertAll(targetIds, id => id.ToString(CultureInfo.InvariantCulture));
        string[] labels = Array.ConvertAll(targetIds, id => id.ToString(CultureInfo.CurrentCulture));
        IReadOnlyList<HistogramGroup> groups = HistogramGroups.ForCategories(keys, labels);

        return (slotCounts, slotCount, groups, FoldGroupMasks(slotColorMask, groups));
    }

    private static (int[] SlotCounts, int SlotCount, IReadOnlyList<HistogramGroup> Groups, uint[]? GroupHighlightMasks) ScanByField(
        IEventColumnView view,
        EventFieldId field,
        Func<IReadOnlyList<string>, IReadOnlyList<string>> labelMapper,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        bool useHighlightTie,
        byte[]? highlightWinners,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        view.CountFieldValues(field, counts, cancellationToken);

        string[] targetValues = counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(HistogramConstants.MaxGroupByCategories)
            .Select(pair => pair.Key)
            .ToArray();

        int slotCount = targetValues.Length + 1;
        int[] slotCounts = new int[bucketCount * slotCount];
        uint[]? slotColorMask = useHighlightTie ? new uint[slotCount] : null;

        if (slotColorMask is null)
        {
            view.BucketTimeTicksByField(minTicks,
                bucketSpanTicks,
                bucketCount,
                field,
                targetValues,
                slotCounts,
                cancellationToken);
        }
        else
        {
            view.BucketTimeTicksByFieldWithTie(RequireHighlightWinners(highlightWinners),
                slotColorMask,
                minTicks,
                bucketSpanTicks,
                bucketCount,
                field,
                targetValues,
                slotCounts,
                cancellationToken);
        }

        IReadOnlyList<HistogramGroup> groups = HistogramGroups.ForCategories(targetValues, labelMapper(targetValues));

        return (slotCounts, slotCount, groups, FoldGroupMasks(slotColorMask, groups));
    }

    private static (int[] SlotCounts, int SlotCount, IReadOnlyList<HistogramGroup> Groups, uint[]? GroupHighlightMasks) ScanSeverity(
        IEventColumnView view,
        long minTicks,
        long bucketSpanTicks,
        int bucketCount,
        bool useHighlightTie,
        byte[]? highlightWinners,
        CancellationToken cancellationToken)
    {
        int slotCount = HistogramGroups.SeveritySlotCount;
        int[] slotCounts = new int[bucketCount * slotCount];
        uint[]? slotColorMask = useHighlightTie ? new uint[slotCount] : null;

        if (slotColorMask is null)
        {
            view.BucketTimeTicksBySeverity(minTicks, bucketSpanTicks, bucketCount, slotCounts, cancellationToken);
        }
        else
        {
            view.BucketTimeTicksBySeverityWithTie(RequireHighlightWinners(highlightWinners),
                slotColorMask,
                minTicks,
                bucketSpanTicks,
                bucketCount,
                slotCounts,
                cancellationToken);
        }

        return (slotCounts, slotCount, HistogramGroups.Severity, FoldGroupMasks(slotColorMask, HistogramGroups.Severity));
    }

    private static string ToEventDataFieldName(HistogramDimension dimension) => dimension switch
    {
        HistogramDimension.LogonType => "LogonType",
        HistogramDimension.TicketEncryptionType => "TicketEncryptionType",
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "Dimension is not an EventData field.")
    };

    private static EventFieldId ToFieldId(HistogramDimension dimension) => dimension switch
    {
        HistogramDimension.Source => EventFieldId.Source,
        HistogramDimension.TaskCategory => EventFieldId.TaskCategory,
        HistogramDimension.Opcode => EventFieldId.Opcode,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "Dimension is not a pooled string field.")
    };
}
