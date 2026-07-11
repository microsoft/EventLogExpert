// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Readers;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;

namespace EventLogExpert.Eventing.Resolvers;

/// <summary>
///     Resolves the human-readable task name and keyword list for an event record from provider metadata plus the
///     Microsoft-defined standard keyword bitmask table.
/// </summary>
/// <remarks>
///     Standard keyword strings are redefined locally to match Windows Event Viewer's rendering, which uses different
///     display names than <see cref="System.Diagnostics.Eventing.Reader.StandardEventKeywords" />.
/// </remarks>
internal sealed class TaskKeywordResolver(IEventResolverCache? cache, ITraceLogger? logger)
{
    private static readonly Dictionary<long, string> s_standardKeywords = new()
    {
        { 0x1000000000000, "Response Time" },
        { 0x2000000000000, "Wdi Context" },
        { 0x4000000000000, "Wdi Diag" },
        { 0x8000000000000, "Sqm" },
        { 0x10000000000000, "Audit Failure" },
        { 0x20000000000000, "Audit Success" },
        { 0x40000000000000, "Correlation Hint" },
        { 0x80000000000000, "Classic" }
    };

    // OS-level standard opcodes (winmeta) that providers do not carry in their own Opcodes table; display text matches
    // Windows Event Viewer.
    private static readonly Dictionary<byte, string> s_standardOpcodes = new()
    {
        { 0, "Info" },
        { 1, "Start" },
        { 2, "Stop" },
        { 3, "DC Start" },
        { 4, "DC Stop" },
        { 5, "Extension" },
        { 6, "Reply" },
        { 7, "Resume" },
        { 8, "Suspend" },
        { 9, "Send" },
        { 240, "Receive" }
    };

    private readonly IEventResolverCache? _cache = cache;
    private readonly ITraceLogger? _logger = logger;

    /// <summary>
    ///     Returns the keyword strings derived from the event record's keyword bitmask, combining Microsoft-defined
    ///     standard keywords (bits 48–55) with provider-defined keywords (bits 0–47). Primary provider wins over supplemental
    ///     on bit conflicts.
    /// </summary>
    public List<string> ResolveKeywords(EventRecord eventRecord, ProviderDetails? details, ProviderDetails? supplemental)
    {
        if (eventRecord.Keywords is null or 0) { return []; }

        var keywordsValue = eventRecord.Keywords.Value;
        List<string> returnValue = [];

        // Standard (Microsoft-defined) keywords live in bits 48–55. Skip the entire
        // standard-keyword scan when the event has no bits in that range.
        if ((keywordsValue & 0x00FF_0000_0000_0000L) != 0)
        {
            foreach (var (bit, name) in s_standardKeywords)
            {
                if ((keywordsValue & bit) != bit) { continue; }

                var keyword = name.TrimEnd('\0');
                returnValue.Add(_cache?.GetOrAddValue(keyword) ?? keyword);
            }
        }

        // Provider-defined keywords use bits 0–47; bits 48–63 are reserved
        // for Microsoft-defined standard keywords handled above.
        var providerBits = keywordsValue & 0x0000_FFFF_FFFF_FFFFL;

        if (providerBits == 0) { return returnValue; }

        long matchedBits = 0;

        if (details is not null)
        {
            foreach (var (bit, name) in details.Keywords)
            {
                if ((providerBits & bit) != bit) { continue; }

                matchedBits |= bit;
                var keyword = name.TrimEnd('\0');
                returnValue.Add(_cache?.GetOrAddValue(keyword) ?? keyword);
            }
        }

        // Fill remaining set bits from supplemental. Primary wins on conflicts.
        if (supplemental is not null && !ReferenceEquals(supplemental, details))
        {
            foreach (var (bit, name) in supplemental.Keywords)
            {
                if ((providerBits & bit) != bit) { continue; }

                if ((matchedBits & bit) == bit) { continue; }

                var keyword = name.TrimEnd('\0');
                returnValue.Add(_cache?.GetOrAddValue(keyword) ?? keyword);
            }
        }

        return returnValue;
    }

    /// <summary>
    ///     Resolves the human-readable opcode name for the event, preferring provider tables then the winmeta standard
    ///     opcodes, falling back to a numeric placeholder.
    /// </summary>
    public string ResolveOpcodeName(
        EventRecord eventRecord,
        ProviderDetails? details,
        EventModel? modernEvent,
        ProviderDetails? supplemental,
        EventModel? supplementalModernEvent)
    {
        if (TryResolveOpcodeNameFromDetails(eventRecord, details, modernEvent, out var opcodeName))
        {
            return CacheOpcodeName(opcodeName);
        }

        if (supplemental is not null &&
            !ReferenceEquals(supplemental, details) &&
            TryResolveOpcodeNameFromDetails(eventRecord, supplemental, supplementalModernEvent, out opcodeName))
        {
            return CacheOpcodeName(opcodeName);
        }

        if (!eventRecord.Opcode.HasValue) { return string.Empty; }

        return s_standardOpcodes.TryGetValue(eventRecord.Opcode.Value, out var standardName)
            ? CacheOpcodeName(standardName)
            : CacheOpcodeName($"({eventRecord.Opcode.Value})");
    }

    /// <summary>
    ///     Resolves the human-readable task name for the event, preferring primary provider tables then supplemental,
    ///     falling back to a numeric placeholder when neither resolves the task.
    /// </summary>
    public string ResolveTaskName(
        EventRecord eventRecord,
        ProviderDetails? details,
        EventModel? modernEvent,
        ProviderDetails? supplemental,
        EventModel? supplementalModernEvent)
    {
        if (TryResolveTaskNameFromDetails(eventRecord, details, modernEvent, out var taskName))
        {
            return CacheTaskName(taskName);
        }

        if (supplemental is not null &&
            !ReferenceEquals(supplemental, details) &&
            TryResolveTaskNameFromDetails(eventRecord, supplemental, supplementalModernEvent, out taskName))
        {
            // The primary modernEvent (if any) was already tried above against primary's tables.
            // Use the pre-computed supplementalModernEvent so its EventModel.Task can drive
            // supplemental's Tasks lookup.
            return CacheTaskName(taskName);
        }

        return !eventRecord.Task.HasValue ?
            string.Empty :
            CacheTaskName(eventRecord.Task == 0 ? "None" : $"({eventRecord.Task})");
    }

    private static bool TryResolveOpcodeNameFromDetails(
        EventRecord eventRecord,
        ProviderDetails? details,
        EventModel? modernEvent,
        out string opcodeName)
    {
        opcodeName = string.Empty;

        if (details is null) { return false; }

        if (modernEvent is not null && details.Opcodes.TryGetValue(modernEvent.Opcode, out var name))
        {
            opcodeName = name;

            return true;
        }

        if (!eventRecord.Opcode.HasValue) { return false; }

        if (details.Opcodes.TryGetValue(eventRecord.Opcode.Value, out name))
        {
            opcodeName = name;

            return true;
        }

        return false;
    }

    private string CacheOpcodeName(string opcodeName)
    {
        opcodeName = opcodeName.TrimEnd('\0');

        return _cache?.GetOrAddValue(opcodeName) ?? opcodeName;
    }

    private string CacheTaskName(string taskName)
    {
        taskName = taskName.TrimEnd('\0');

        return _cache?.GetOrAddValue(taskName) ?? taskName;
    }

    private bool TryResolveTaskNameFromDetails(
        EventRecord eventRecord,
        ProviderDetails? details,
        EventModel? modernEvent,
        out string taskName)
    {
        taskName = string.Empty;

        if (details is null) { return false; }

        if (modernEvent?.Task is not null && details.Tasks.TryGetValue(modernEvent.Task, out var name))
        {
            taskName = name;

            return true;
        }

        if (!eventRecord.Task.HasValue) { return false; }

        if (details.Tasks.TryGetValue(eventRecord.Task.Value, out name))
        {
            taskName = name;

            return true;
        }

        var messagesByShortId = details.GetMessagesByShortId(eventRecord.Task.Value);

        List<MessageModel>? potentialTaskNames = null;

        foreach (var m in messagesByShortId)
        {
            if (m.LogLink is null || m.LogLink != eventRecord.LogName) { continue; }

            potentialTaskNames ??= [];
            potentialTaskNames.Add(m);
        }

        if (potentialTaskNames is not { Count: > 0 }) { return false; }

        taskName = potentialTaskNames[0].Text;

        if (potentialTaskNames.Count <= 1) { return true; }

        _logger?.Debug($"More than one matching task ID was found.");
        _logger?.Debug($"  eventRecord.Task: {eventRecord.Task}");
        _logger?.Debug($"   Potential matches:");

        potentialTaskNames.ForEach(t => _logger?.Debug($"    {t.LogLink} {t.Text}"));

        return true;
    }
}
