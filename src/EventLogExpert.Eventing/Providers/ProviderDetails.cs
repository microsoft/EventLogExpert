// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using System.ComponentModel.DataAnnotations.Schema;

namespace EventLogExpert.Eventing.Providers;

public class ProviderDetails
{
    private IReadOnlyList<EventModel> _events = [];
    private Dictionary<long, List<EventModel>>? _eventsByIdLookup;
    private IReadOnlyList<MessageModel> _messages = [];
    private Dictionary<int, List<MessageModel>>? _messagesByShortIdLookup;

    /// <summary>Events and related items from modern provider</summary>
    public IReadOnlyList<EventModel> Events
    {
        get => _events;
        set
        {
            _events = value;
            _eventsByIdLookup = null;
        }
    }

    /// <summary>True when no provider metadata of any kind is loaded (legacy or modern).
    /// Used by resolvers to distinguish "provider not found" from "provider known but no matching message".</summary>
    public bool IsEmpty =>
        Events.Count == 0 &&
        Messages.Count == 0 &&
        Keywords.Count == 0 &&
        Opcodes.Count == 0 &&
        Tasks.Count == 0 &&
        !Parameters.Any();

    public IDictionary<long, string> Keywords { get; set; } = new Dictionary<long, string>();

    /// <summary>Messages from legacy provider</summary>
    public IReadOnlyList<MessageModel> Messages
    {
        get => _messages;
        set
        {
            _messages = value;
            _messagesByShortIdLookup = null;
        }
    }

    public IDictionary<int, string> Opcodes { get; set; } = new Dictionary<int, string>();

    /// <summary>Parameter strings from legacy provider</summary>
    public IEnumerable<MessageModel> Parameters { get; set; } = [];

    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// When the provider name on the event was actually a channel path that the resolver
    /// followed back to its owning publisher, this records the publisher name that the
    /// metadata/messages were ultimately loaded from. Null when no channel-owner fallback
    /// was used. Diagnostic only — does not participate in any cache key or equality check,
    /// and is excluded from the EF Core schema so existing provider databases are unaffected.
    /// </summary>
    [NotMapped]
    public string? ResolvedFromOwningPublisher { get; set; } // TODO: Should probably be mapped in the DB upgrade

    public IDictionary<int, string> Tasks { get; set; } = new Dictionary<int, string>();

    /// <summary>Gets events matching the given Id using a pre-built lookup dictionary.</summary>
    internal IReadOnlyList<EventModel> GetEventsById(long id)
    {
        _eventsByIdLookup ??= BuildEventsByIdLookup();

        return _eventsByIdLookup.TryGetValue(id, out var list) ? list : [];
    }

    /// <summary>Gets messages matching the given ShortId using a pre-built lookup dictionary.
    /// The lookup uses int keys derived from unsigned reinterpretation of ShortId,
    /// matching the implicit ushort-to-int promotion used by callers.</summary>
    internal IReadOnlyList<MessageModel> GetMessagesByShortId(int shortId)
    {
        _messagesByShortIdLookup ??= BuildMessagesByShortIdLookup();

        return _messagesByShortIdLookup.TryGetValue(shortId, out var list) ? list : [];
    }

    private Dictionary<long, List<EventModel>> BuildEventsByIdLookup()
    {
        var lookup = new Dictionary<long, List<EventModel>>();

        foreach (var e in _events)
        {
            if (!lookup.TryGetValue(e.Id, out var list))
            {
                list = [];
                lookup[e.Id] = list;
            }

            list.Add(e);
        }

        return lookup;
    }

    private Dictionary<int, List<MessageModel>> BuildMessagesByShortIdLookup()
    {
        var lookup = new Dictionary<int, List<MessageModel>>();

        foreach (var m in _messages)
        {
            // Reinterpret ShortId as unsigned to match ushort-to-int promotion
            // used by callers (EventRecord.Id and Task are ushort).
            int key = (ushort)m.ShortId;

            if (!lookup.TryGetValue(key, out var list))
            {
                list = [];
                lookup[key] = list;
            }

            list.Add(m);
        }

        return lookup;
    }
}
