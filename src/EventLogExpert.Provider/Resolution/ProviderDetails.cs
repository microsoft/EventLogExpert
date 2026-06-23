// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.ObjectModel;

namespace EventLogExpert.Provider.Resolution;

public sealed class ProviderDetails
{
    private IReadOnlyList<EventModel> _events = [];
    private Dictionary<long, List<EventModel>>? _eventsByIdLookup;
    private ILazyMessageSource? _messageStore;
    private IReadOnlyList<MessageModel>? _messagesView;
    private ILazyMessageSource? _parameterStore;
    private IReadOnlyList<MessageModel>? _parametersView;

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
        (_messageStore?.Count ?? 0) == 0 &&
        Keywords.Count == 0 &&
        Opcodes.Count == 0 &&
        Tasks.Count == 0 &&
        (_parameterStore?.Count ?? 0) == 0 &&
        ResolvedFromOwningPublisher is null;

    public IDictionary<long, string> Keywords { get; set; } = new Dictionary<long, string>();

    public IReadOnlyDictionary<string, ValueMapDefinition> Maps { get; set; } =
        ReadOnlyDictionary<string, ValueMapDefinition>.Empty;

    public IReadOnlyList<MessageModel> Messages
    {
        get => _messagesView ?? [];
        set
        {
            _messageStore = CompactMessageStore.Build(value);
            _messagesView = _messageStore.AsView();
        }
    }

    public ILazyMessageSource? MessageSource => _messageStore;

    public IDictionary<int, string> Opcodes { get; set; } = new Dictionary<int, string>();

    public IReadOnlyList<MessageModel> Parameters
    {
        get => _parametersView ?? [];
        set
        {
            _parameterStore = CompactMessageStore.Build(value);
            _parametersView = _parameterStore.AsView();
        }
    }

    public ILazyMessageSource? ParameterSource => _parameterStore;

    public string ProviderName { get; set; } = string.Empty;

    public string? ResolvedFromOwningPublisher { get; set; }

    public IDictionary<int, string> Tasks { get; set; } = new Dictionary<int, string>();

    /// <summary>
    ///     Content version of this provider - a hash of the rendering payload, stamped at db-create, so identical
    ///     providers collapse to one row and genuinely different versions coexist. Empty for single-version / legacy
    ///     databases.
    /// </summary>
    public string VersionKey { get; set; } = string.Empty;

    /// <summary>Gets events matching the given Id using a pre-built lookup dictionary.</summary>
    public IReadOnlyList<EventModel> GetEventsById(long id)
    {
        _eventsByIdLookup ??= BuildEventsByIdLookup();

        return _eventsByIdLookup.TryGetValue(id, out var list) ? list : [];
    }

    /// <summary>
    ///     Gets messages matching the given ShortId (compared as unsigned, matching the implicit ushort-to-int promotion
    ///     used by callers). Materialized on demand from the compact store and cached.
    /// </summary>
    public IReadOnlyList<MessageModel> GetMessagesByShortId(int shortId) =>
        _messageStore?.GetByShortId(shortId) ?? [];

    /// <summary>
    ///     Gets the first parameter message with the given RawId, or null when none matches. Duplicate RawIds resolve
    ///     first-wins. Materialized on demand from the compact store and cached.
    /// </summary>
    public MessageModel? GetParameterByRawId(long rawId) => _parameterStore?.GetByRawIdFirst(rawId);

    public void SetLazyMessageSource(ILazyMessageSource source)
    {
        _messageStore = source;
        _messagesView = source.AsView();
    }

    public void SetLazyParameterSource(ILazyMessageSource source)
    {
        _parameterStore = source;
        _parametersView = source.AsView();
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
}
