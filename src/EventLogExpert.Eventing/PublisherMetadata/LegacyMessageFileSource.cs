// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;
using System.Collections;
using System.Collections.Concurrent;

namespace EventLogExpert.Eventing.PublisherMetadata;

internal sealed class LegacyMessageFileSource : ILazyMessageSource
{
    private static readonly IReadOnlyList<MessageModel> s_empty = [];

    private readonly Lazy<IReadOnlyList<MessageModel>> _all;
    private readonly ConcurrentDictionary<long, MessageModel?> _byRawId = new();
    private readonly ConcurrentDictionary<int, IReadOnlyList<MessageModel>> _byShortId = new();
    private readonly int _count;
    private readonly IReadOnlyList<string> _files;
    private readonly ITraceLogger? _logger;
    private readonly string _providerName;
    private IReadOnlyList<MessageModel>? _view;

    internal LegacyMessageFileSource(IReadOnlyList<string> files, string providerName, int count, ITraceLogger? logger)
    {
        _files = files;
        _providerName = providerName;
        _count = count;
        _logger = logger;
        _all = new Lazy<IReadOnlyList<MessageModel>>(MaterializeAllCore);
    }

    public int Count => _count;

    public IReadOnlyList<MessageModel> AsView() => _view ??= new LazyMessageView(_count, this);

    public MessageModel? GetByRawIdFirst(long rawId) =>
        _byRawId.GetOrAdd(rawId, static (id, self) => self.ExtractByRawId(id), this);

    public IReadOnlyList<MessageModel> GetByShortId(int shortId) =>
        _byShortId.GetOrAdd(shortId, static (id, self) => self.ExtractByShortId(id), this);

    public IReadOnlyList<MessageModel> MaterializeAll() => _all.Value;

    private MessageModel? ExtractByRawId(long rawId)
    {
        foreach (var file in _files)
        {
            if (!MessageTableReader.TryOpen(file, _logger, out var handle, out var memTable, out uint size)) { continue; }

            try
            {
                var match = MessageTableReader.FindFirstByRawId(memTable, size, rawId, _providerName);
                if (match is not null) { return match; }
            }
            finally { handle.Dispose(); }
        }

        return null;
    }

    private IReadOnlyList<MessageModel> ExtractByShortId(int shortId)
    {
        var result = new List<MessageModel>();

        foreach (var file in _files)
        {
            if (!MessageTableReader.TryOpen(file, _logger, out var handle, out var memTable, out uint size)) { continue; }

            try { MessageTableReader.AppendMatches(memTable, size, _providerName, shortId, result); }
            finally { handle.Dispose(); }
        }

        return result.Count == 0 ? s_empty : result;
    }

    private IReadOnlyList<MessageModel> MaterializeAllCore()
    {
        var result = new List<MessageModel>(_count);

        foreach (var file in _files)
        {
            if (!MessageTableReader.TryOpen(file, _logger, out var handle, out var memTable, out uint size)) { continue; }

            try { MessageTableReader.AppendMatches(memTable, size, _providerName, -1, result); }
            finally { handle.Dispose(); }
        }

        return result;
    }

    private sealed class LazyMessageView(int count, LegacyMessageFileSource source) : IReadOnlyList<MessageModel>
    {
        public int Count => count;

        public MessageModel this[int index] => source.MaterializeAll()[index];

        public IEnumerator<MessageModel> GetEnumerator() => source.MaterializeAll().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
