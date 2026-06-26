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

    internal static LegacyMessageFileSource? TryCreate(IReadOnlyList<string> files, string providerName, ITraceLogger? logger)
    {
        if (files.Count == 0) { return null; }

        var walkable = new List<string>();
        int total = 0;

        foreach (var (file, memTable, size) in OpenMessageTables(files, logger))
        {
            int count = MessageTableReader.CountEntries(memTable, size);

            if (count > 0)
            {
                walkable.Add(file);
                total += count;
            }
        }

        return total > 0 ? new LegacyMessageFileSource(walkable, providerName, total, logger) : null;
    }

    private static IEnumerable<(string File, nint MemTable, uint Size)> OpenMessageTables(
        IReadOnlyList<string> files,
        ITraceLogger? logger)
    {
        foreach (var file in files)
        {
            if (!MessageTableReader.TryOpen(file, logger, out var handle, out var memTable, out uint size)) { continue; }

            try { yield return (file, memTable, size); }
            finally { handle.Dispose(); }
        }
    }

    private MessageModel? ExtractByRawId(long rawId)
    {
        foreach (var (_, memTable, size) in OpenMessageTables(_files, _logger))
        {
            var match = MessageTableReader.FindFirstByRawId(memTable, size, rawId, _providerName);

            if (match is not null) { return match; }
        }

        return null;
    }

    private IReadOnlyList<MessageModel> ExtractByShortId(int shortId)
    {
        var result = new List<MessageModel>();

        foreach (var (_, memTable, size) in OpenMessageTables(_files, _logger))
        {
            MessageTableReader.AppendMatches(memTable, size, _providerName, shortId, result);
        }

        return result.Count == 0 ? s_empty : result;
    }

    private IReadOnlyList<MessageModel> MaterializeAllCore()
    {
        var result = new List<MessageModel>(_count);

        foreach (var (_, memTable, size) in OpenMessageTables(_files, _logger))
        {
            MessageTableReader.AppendMatches(memTable, size, _providerName, -1, result);
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
