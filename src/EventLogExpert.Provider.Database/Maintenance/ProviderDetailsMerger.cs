// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Models;
using EventLogExpert.Provider.Schema;

namespace EventLogExpert.ProviderDatabase.Maintenance;

internal static class ProviderDetailsMerger
{
    public static List<ProviderDetails> MergeCaseInsensitiveDuplicates(
        IReadOnlyList<ProviderDetails> rows,
        string databasePath)
    {
        var merged = new List<ProviderDetails>(rows.Count);
        var firstIndexByGroup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var groupedRows = new Dictionary<string, List<ProviderDetails>>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var key = row.ProviderName;

            if (firstIndexByGroup.TryAdd(key, i))
            {
                groupedRows[key] = [row];
            }
            else
            {
                groupedRows[key].Add(row);
            }
        }

        foreach (var (_, firstIndex) in firstIndexByGroup.OrderBy(kvp => kvp.Value))
        {
            var groupKey = rows[firstIndex].ProviderName;
            var group = groupedRows[groupKey];

            if (group.Count == 1)
            {
                merged.Add(group[0]);

                continue;
            }

            merged.Add(MergeGroup(group, databasePath));
        }

        return merged;
    }

    private static bool EventsAreEquivalent(EventModel a, EventModel b) =>
        a.Level == b.Level &&
        a.Opcode == b.Opcode &&
        a.Task == b.Task &&
        KeywordsEqual(a.Keywords, b.Keywords) &&
        string.Equals(a.Template, b.Template, StringComparison.Ordinal) &&
        string.Equals(a.Description, b.Description, StringComparison.Ordinal);

    private static bool KeywordsEqual(long[] a, long[] b)
    {
        if (a.Length == 0 && b.Length == 0) { return true; }

        var setA = new HashSet<long>(a);
        var setB = new HashSet<long>(b);

        return setA.SetEquals(setB);
    }

    private static IReadOnlyList<EventModel> MergeEvents(
        IEnumerable<EventModel> events,
        string canonicalProviderName,
        string databasePath)
    {
        var seen = new Dictionary<EventIdentity, EventModel>();

        foreach (var evt in events)
        {
            var identity = new EventIdentity(evt.Id, evt.Version, evt.LogName);

            if (!seen.TryGetValue(identity, out var existing))
            {
                seen[identity] = evt;

                continue;
            }

            if (!EventsAreEquivalent(existing, evt))
            {
                throw new DatabaseUpgradeException(
                    databasePath,
                    $"Provider '{canonicalProviderName}' has conflicting Event rows for Id={evt.Id}, " +
                    $"Version={evt.Version}, LogName='{evt.LogName}'. " +
                    $"Cannot merge case-insensitive duplicates with different Level/Opcode/Task/Keywords/Template/Description values.");
            }
        }

        return seen.Values.ToList();
    }

    private static ProviderDetails MergeGroup(List<ProviderDetails> group, string databasePath)
    {
        var canonicalName = group[0].ProviderName;

        return new ProviderDetails
        {
            ProviderName = canonicalName,
            Messages = MergeMessages(group.SelectMany(r => r.Messages), canonicalName, databasePath),
            Parameters = MergeMessages(group.SelectMany(r => r.Parameters), canonicalName, databasePath),
            Events = MergeEvents(group.SelectMany(r => r.Events), canonicalName, databasePath),
            Keywords = MergeStringDictionary(
                group.Select(r => r.Keywords),
                canonicalName,
                databasePath,
                "Keywords",
                key => key.ToString()),
            Opcodes = MergeStringDictionary(
                group.Select(r => r.Opcodes),
                canonicalName,
                databasePath,
                "Opcodes",
                key => key.ToString()),
            Tasks = MergeStringDictionary(
                group.Select(r => r.Tasks),
                canonicalName,
                databasePath,
                "Tasks",
                key => key.ToString()),
            ResolvedFromOwningPublisher = MergeResolvedFromOwningPublisher(
                group.Select(r => r.ResolvedFromOwningPublisher),
                canonicalName,
                databasePath)
        };
    }

    private static IReadOnlyList<MessageModel> MergeMessages(
        IEnumerable<MessageModel> messages,
        string canonicalProviderName,
        string databasePath)
    {
        var seen = new Dictionary<MessageIdentity, MessageModel>();

        foreach (var message in messages)
        {
            var identity = new MessageIdentity(message.ShortId, message.RawId, message.LogLink, message.Tag);

            if (!seen.TryGetValue(identity, out var existing))
            {
                seen[identity] = message;
                continue;
            }

            if (!MessagesAreEquivalent(existing, message))
            {
                throw new DatabaseUpgradeException(
                    databasePath,
                    $"Provider '{canonicalProviderName}' has conflicting Message rows for ShortId={message.ShortId}, " +
                    $"RawId={message.RawId}, LogLink='{message.LogLink}', Tag='{message.Tag}'. " +
                    $"Cannot merge case-insensitive duplicates with different Text or Template values.");
            }
        }

        return seen.Values.ToList();
    }

    private static string? MergeResolvedFromOwningPublisher(
        IEnumerable<string?> values,
        string canonicalProviderName,
        string databasePath)
    {
        string? winner = null;

        foreach (var value in values)
        {
            if (string.IsNullOrEmpty(value)) { continue; }

            if (winner is null)
            {
                winner = value;
                continue;
            }

            if (!string.Equals(winner, value, StringComparison.Ordinal))
            {
                throw new DatabaseUpgradeException(
                    databasePath,
                    $"Provider '{canonicalProviderName}' has conflicting ResolvedFromOwningPublisher values: " +
                    $"'{winner}' vs '{value}'.");
            }
        }

        return winner;
    }

    private static IDictionary<TKey, string> MergeStringDictionary<TKey>(
        IEnumerable<IDictionary<TKey, string>> dictionaries,
        string canonicalProviderName,
        string databasePath,
        string memberName,
        Func<TKey, string> keyDescriber)
        where TKey : notnull
    {
        var merged = new Dictionary<TKey, string>();

        foreach (var dict in dictionaries)
        {
            foreach (var (key, value) in dict)
            {
                if (!merged.TryGetValue(key, out var existing))
                {
                    merged[key] = value;

                    continue;
                }

                if (!string.Equals(existing, value, StringComparison.Ordinal))
                {
                    throw new DatabaseUpgradeException(
                        databasePath,
                        $"Provider '{canonicalProviderName}' has conflicting {memberName} entries for key {keyDescriber(key)}: " +
                        $"'{existing}' vs '{value}'.");
                }
            }
        }

        return merged;
    }

    private static bool MessagesAreEquivalent(MessageModel a, MessageModel b) =>
        string.Equals(a.Text, b.Text, StringComparison.Ordinal) &&
        string.Equals(a.Template, b.Template, StringComparison.Ordinal);

    private readonly record struct MessageIdentity(short ShortId, long RawId, string? LogLink, string? Tag);

    private readonly record struct EventIdentity(long Id, byte Version, string? LogName);
}
