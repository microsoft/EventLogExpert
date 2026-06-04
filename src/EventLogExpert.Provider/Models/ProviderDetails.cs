// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Provider.Models;

public sealed class ProviderDetails
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

    public bool IsEmpty =>
        Events.Count == 0 &&
        Messages.Count == 0 &&
        Keywords.Count == 0 &&
        Opcodes.Count == 0 &&
        Tasks.Count == 0 &&
        !Parameters.Any() &&
        ResolvedFromOwningPublisher is null;

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

    public string? ResolvedFromOwningPublisher { get; set; }

    public IDictionary<int, string> Tasks { get; set; } = new Dictionary<int, string>();

    /// <summary>Gets events matching the given Id using a pre-built lookup dictionary.</summary>
    public IReadOnlyList<EventModel> GetEventsById(long id)
    {
        _eventsByIdLookup ??= BuildEventsByIdLookup();

        return _eventsByIdLookup.TryGetValue(id, out var list) ? list : [];
    }

    /// <summary>
    ///     Gets messages matching the given ShortId using a pre-built lookup dictionary. The lookup uses int keys derived
    ///     from unsigned reinterpretation of ShortId, matching the implicit ushort-to-int promotion used by callers.
    /// </summary>
    public IReadOnlyList<MessageModel> GetMessagesByShortId(int shortId)
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
