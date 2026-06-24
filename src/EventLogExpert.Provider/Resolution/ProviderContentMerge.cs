// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Provider.Resolution;

/// <summary>
///     Shared deduplication primitives for collapsing the content of two or more provider rows that describe the same
///     logical provider. Two callers consume these: the database upgrade/merge path (which treats a non-equivalent
///     collision as corruption and throws) and, later, the resolve-time multi-version union (which treats a non-equivalent
///     collision as legitimate version divergence and records it). Keeping the identity keys and equivalence rules in one
///     place stops the two paths from drifting - a field one path collapses on but the other does not would desynchronize
///     merge from union and silently change which row wins.
/// </summary>
public static class ProviderContentMerge
{
    /// <summary>
    ///     Deduplicates <paramref name="items" /> by identity, keeping the first occurrence of each identity in
    ///     first-seen order (matching the prior <c>Dictionary.Values</c> enumeration with no removals). When a later item
    ///     shares an earlier item's identity but is not equivalent to it, <paramref name="onConflict" /> is invoked
    ///     synchronously with (existing, duplicate) - so a throwing callback short-circuits the merge exactly as before - and
    ///     the existing winner is otherwise retained.
    /// </summary>
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
        string.Equals(left.Template, right.Template, StringComparison.Ordinal) &&
        string.Equals(left.Description, right.Description, StringComparison.Ordinal);

    /// <summary>Extracts the <see cref="MessageIdentity" /> of a message row.</summary>
    public static MessageIdentity IdentityOf(MessageModel message) =>
        new(message.ShortId, message.RawId, message.LogLink, message.Tag);

    /// <summary>Extracts the <see cref="EventIdentity" /> of an event row.</summary>
    public static EventIdentity IdentityOf(EventModel @event) =>
        new(@event.Id, @event.Version, @event.LogName);

    public static bool MapsAreEquivalent(ValueMapDefinition left, ValueMapDefinition right) =>
        left.IsBitMap == right.IsBitMap && left.Entries.SequenceEqual(right.Entries);

    /// <summary>
    ///     Merges value-map dictionaries (ordinal key comparison), keeping the first definition seen for each name. When
    ///     a later dictionary maps a known name to a non-equivalent definition, <paramref name="onConflict" /> is invoked with
    ///     (name, existing, duplicate).
    /// </summary>
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

    /// <summary>
    ///     Picks the first non-empty value, invoking <paramref name="onConflict" /> with (winner, duplicate) when a later
    ///     non-empty value disagrees with the winner. Returns null when every value is null or empty.
    /// </summary>
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

    /// <summary>
    ///     Merges string-valued dictionaries, keeping the first value seen for each key (default
    ///     <typeparamref name="TKey" /> equality). When a later dictionary maps a known key to a different value,
    ///     <paramref name="onConflict" /> is invoked with (key, existing, duplicate).
    /// </summary>
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

    /// <summary>Identity of a message-table row, matching the database merge key exactly.</summary>
    public readonly record struct MessageIdentity(short ShortId, long RawId, string? LogLink, string? Tag);

    /// <summary>Identity of a modern event row, matching the database merge key exactly.</summary>
    public readonly record struct EventIdentity(long Id, byte Version, string? LogName);
}
