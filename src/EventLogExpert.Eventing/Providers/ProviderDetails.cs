// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;

namespace EventLogExpert.Eventing.Providers;

public class ProviderDetails
{
    private IReadOnlyList<EventModel> _events = [];
    private IReadOnlyList<MessageModel> _messages = [];
    private Dictionary<long, List<EventModel>>? _eventsByIdLookup;
    private Dictionary<short, List<MessageModel>>? _messagesByShortIdLookup;

    public string ProviderName { get; set; } = string.Empty;

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

    /// <summary>Parameter strings from legacy provider</summary>
    public IEnumerable<MessageModel> Parameters { get; set; } = [];

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

    public IDictionary<long, string> Keywords { get; set; } = new Dictionary<long, string>();

    public IDictionary<int, string> Opcodes { get; set; } = new Dictionary<int, string>();

    public IDictionary<int, string> Tasks { get; set; } = new Dictionary<int, string>();

    /// <summary>Gets events matching the given Id using a pre-built lookup dictionary.</summary>
    internal IReadOnlyList<EventModel> GetEventsById(long id)
    {
        _eventsByIdLookup ??= BuildEventsByIdLookup();

        return _eventsByIdLookup.TryGetValue(id, out var list) ? list : [];
    }

    /// <summary>Gets messages matching the given ShortId using a pre-built lookup dictionary.</summary>
    internal IReadOnlyList<MessageModel> GetMessagesByShortId(short shortId)
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

    private Dictionary<short, List<MessageModel>> BuildMessagesByShortIdLookup()
    {
        var lookup = new Dictionary<short, List<MessageModel>>();

        foreach (var m in _messages)
        {
            if (!lookup.TryGetValue(m.ShortId, out var list))
            {
                list = [];
                lookup[m.ShortId] = list;
            }

            list.Add(m);
        }

        return lookup;
    }
}
