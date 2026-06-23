// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Resolution;
using EventLogExpert.Provider.Schema;

namespace EventLogExpert.ProviderDatabase.Maintenance;

internal static class ProviderDetailsMerger
{
    public static List<ProviderDetails> MergeCaseInsensitiveDuplicates(
        IReadOnlyList<ProviderDetails> rows,
        string databasePath)
    {
        var merged = new List<ProviderDetails>(rows.Count);
        var firstIndexByGroup = new Dictionary<(string ProviderName, string VersionKey), int>(ProviderIdentityComparer.Instance);
        var groupedRows = new Dictionary<(string ProviderName, string VersionKey), List<ProviderDetails>>(ProviderIdentityComparer.Instance);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var key = (row.ProviderName, row.VersionKey);

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
            var first = rows[firstIndex];
            var group = groupedRows[(first.ProviderName, first.VersionKey)];

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

    private static bool MapsAreEquivalent(ValueMapDefinition a, ValueMapDefinition b) =>
        a.IsBitMap == b.IsBitMap && a.Entries.SequenceEqual(b.Entries);

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

        // Rows in a group share the same (ProviderName, VersionKey) identity (the grouping key), so the merged row
        // carries that VersionKey forward; only name case-folding differences and duplicate content are collapsed.
        return new ProviderDetails
        {
            ProviderName = canonicalName,
            VersionKey = group[0].VersionKey,
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
            Maps = MergeMaps(group.Select(r => r.Maps), canonicalName, databasePath),
            ResolvedFromOwningPublisher = MergeResolvedFromOwningPublisher(
                group.Select(r => r.ResolvedFromOwningPublisher),
                canonicalName,
                databasePath)
        };
    }

    private static IReadOnlyDictionary<string, ValueMapDefinition> MergeMaps(
        IEnumerable<IReadOnlyDictionary<string, ValueMapDefinition>> maps,
        string canonicalProviderName,
        string databasePath)
    {
        var merged = new Dictionary<string, ValueMapDefinition>(StringComparer.Ordinal);

        foreach (var map in maps)
        {
            foreach (var (name, definition) in map)
            {
                if (!merged.TryGetValue(name, out var existing))
                {
                    merged[name] = definition;

                    continue;
                }

                if (!MapsAreEquivalent(existing, definition))
                {
                    throw new DatabaseUpgradeException(
                        databasePath,
                        $"Provider '{canonicalProviderName}' has conflicting Map entries for '{name}'. " +
                        $"Cannot merge case-insensitive duplicates with different value-map definitions.");
                }
            }
        }

        return merged;
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

    private sealed class ProviderIdentityComparer : IEqualityComparer<(string ProviderName, string VersionKey)>
    {
        public static readonly ProviderIdentityComparer Instance = new();

        public bool Equals((string ProviderName, string VersionKey) x, (string ProviderName, string VersionKey) y) =>
            string.Equals(x.ProviderName, y.ProviderName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.VersionKey, y.VersionKey, StringComparison.Ordinal);

        public int GetHashCode((string ProviderName, string VersionKey) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ProviderName),
                StringComparer.Ordinal.GetHashCode(obj.VersionKey));
    }

    private readonly record struct MessageIdentity(short ShortId, long RawId, string? LogLink, string? Tag);

    private readonly record struct EventIdentity(long Id, byte Version, string? LogName);
}
