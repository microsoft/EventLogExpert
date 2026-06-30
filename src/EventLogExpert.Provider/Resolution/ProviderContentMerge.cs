// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Provider.Resolution;

// Merge and resolve-time union share identity/equivalence rules so row selection cannot drift.
public static class ProviderContentMerge
{
    // Keeps first identity in first-seen order; conflicts invoke the callback before retaining the winner.
    public static List<TModel> Deduplicate<TIdentity, TModel>(
        IEnumerable<TModel> items,
        Func<TModel, TIdentity> identitySelector,
        Func<TModel, TModel, bool> areEquivalent,
        Action<TModel, TModel> onConflict)
        where TIdentity : notnull
    {
        var winners = new Dictionary<TIdentity, TModel>();
        var order = new List<TIdentity>();

        foreach (var item in items)
        {
            var identity = identitySelector(item);

            if (!winners.TryGetValue(identity, out var existing))
            {
                winners[identity] = item;
                order.Add(identity);

                continue;
            }

            if (!areEquivalent(existing, item))
            {
                onConflict(existing, item);
            }
        }

        var result = new List<TModel>(order.Count);

        foreach (var identity in order)
        {
            result.Add(winners[identity]);
        }

        return result;
    }

    public static bool EventsAreEquivalent(EventModel left, EventModel right) =>
        left.Level == right.Level &&
        left.Opcode == right.Opcode &&
        left.Task == right.Task &&
        KeywordsEqual(left.Keywords, right.Keywords) &&
        TemplateSignature.Equal(left.Template.AsSpan(), right.Template.AsSpan()) &&
        string.Equals(left.Description, right.Description, StringComparison.Ordinal);

    public static MessageIdentity IdentityOf(MessageModel message) =>
        new(message.ShortId, message.RawId, message.LogLink, message.Tag);

    public static EventIdentity IdentityOf(EventModel @event) =>
        new(@event.Id, @event.Version, @event.LogName);

    public static bool MapsAreEquivalent(ValueMapDefinition left, ValueMapDefinition right) =>
        left.IsBitMap == right.IsBitMap && left.Entries.SequenceEqual(right.Entries);

    public static Dictionary<string, ValueMapDefinition> MergeMaps(
        IEnumerable<IReadOnlyDictionary<string, ValueMapDefinition>> maps,
        Action<string, ValueMapDefinition, ValueMapDefinition> onConflict)
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
                    onConflict(name, existing, definition);
                }
            }
        }

        return merged;
    }

    public static string? MergeScalarFirstNonEmpty(IEnumerable<string?> values, Action<string, string> onConflict)
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
                onConflict(winner, value);
            }
        }

        return winner;
    }

    public static Dictionary<TKey, string> MergeStringDictionary<TKey>(
        IEnumerable<IDictionary<TKey, string>> dictionaries,
        Action<TKey, string, string> onConflict)
        where TKey : notnull
    {
        var merged = new Dictionary<TKey, string>();

        foreach (var dictionary in dictionaries)
        {
            foreach (var (key, value) in dictionary)
            {
                if (!merged.TryGetValue(key, out var existing))
                {
                    merged[key] = value;

                    continue;
                }

                if (!string.Equals(existing, value, StringComparison.Ordinal))
                {
                    onConflict(key, existing, value);
                }
            }
        }

        return merged;
    }

    public static bool MessagesAreEquivalent(MessageModel left, MessageModel right) =>
        string.Equals(left.Text, right.Text, StringComparison.Ordinal) &&
        string.Equals(left.Template, right.Template, StringComparison.Ordinal);

    private static bool KeywordsEqual(long[] left, long[] right)
    {
        if (left.Length == 0 && right.Length == 0) { return true; }

        return new HashSet<long>(left).SetEquals(right);
    }

    public readonly record struct MessageIdentity(short ShortId, long RawId, string? LogLink, string? Tag);

    public readonly record struct EventIdentity(long Id, byte Version, string? LogName);
}
