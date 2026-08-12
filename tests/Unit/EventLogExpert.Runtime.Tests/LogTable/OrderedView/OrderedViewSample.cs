// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.Tests.LogTable.OrderedView;

internal sealed class OrderedViewSample
{
    private static readonly Guid[] s_activityIds =
        [new("11111111-1111-1111-1111-111111111111"), new("22222222-2222-2222-2222-222222222222")];
    private static readonly string[] s_computers = ["HOST-1", "HOST-2"];
    private static readonly string[][] s_keywords = [[], ["Audit Success"], ["Audit Failure"]];
    private static readonly string[] s_levels = ["Information", "Warning", "Error", "Critical", ""];
    private static readonly string[] s_sources = ["Provider.A", "Provider.B", "Provider.C"];
    private static readonly string[] s_tasks = ["", "Logon", "Service"];

    private readonly bool _allowNulls;
    private readonly bool _allowTies;
    private readonly EventLogId[] _logIds;
    private readonly long[] _nextRecordId;
    private readonly List<List<ResolvedEvent>> _perLog;
    private readonly Random _rng;

    private DateTime _clock = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public OrderedViewSample(int seed, int logCount, bool allowTies = true, bool allowNulls = true)
    {
        _rng = new Random(seed);
        _allowTies = allowTies;
        _allowNulls = allowNulls;
        _perLog = new List<List<ResolvedEvent>>(logCount);
        _logIds = new EventLogId[logCount];
        _nextRecordId = new long[logCount];

        for (int k = 0; k < logCount; k++)
        {
            _perLog.Add([]);
            _logIds[k] = EventLogId.Create();
            _nextRecordId[k] = 1;
        }
    }

    public int LogCount => _perLog.Count;

    public void Append(int logIndex, int count)
    {
        for (int i = 0; i < count; i++) { AppendOne(logIndex); }
    }

    public IReadOnlyList<ResolvedEvent> Events(int logIndex) => _perLog[logIndex];

    public EventLocator[] Locators(int logIndex, int generation = 0)
    {
        var locators = new EventLocator[_perLog[logIndex].Count];

        for (int i = 0; i < locators.Length; i++) { locators[i] = new EventLocator(_logIds[logIndex], generation, i); }

        return locators;
    }

    public EventLogId LogId(int logIndex) => _logIds[logIndex];

    public IEventColumnReader PrefixReader(int logIndex, int count, int generation = 0, long contentVersion = 0) =>
        EventColumnStore.Build(_perLog[logIndex].Take(count).ToList(), generation, contentVersion)
            .CreateReader(_logIds[logIndex]);

    public IEventColumnReader Reader(int logIndex, int generation = 0, long contentVersion = 0) =>
        EventColumnStore.Build(_perLog[logIndex], generation, contentVersion).CreateReader(_logIds[logIndex]);

    public void SeedInterleaved(int totalEvents)
    {
        for (int k = 0; k < _perLog.Count; k++) { AppendOne(k); }

        for (int i = _perLog.Count; i < totalEvents; i++) { AppendOne(_rng.Next(_perLog.Count)); }
    }

    private void AppendOne(int logIndex)
    {
        _clock = _clock.AddMilliseconds(_allowTies && _rng.Next(4) == 0 ? 0 : 1 + _rng.Next(50));

        long? recordId = _allowNulls && _rng.Next(12) == 0 ? null : _nextRecordId[logIndex]++;

        _perLog[logIndex].Add(new ResolvedEvent($"Log{logIndex}", LogPathType.Channel)
        {
            RecordId = recordId,
            TimeCreated = _clock,
            Id = 1000 + _rng.Next(5),
            Level = s_levels[_rng.Next(s_levels.Length)],
            Source = s_sources[_rng.Next(s_sources.Length)],
            TaskCategory = s_tasks[_rng.Next(s_tasks.Length)],
            ComputerName = s_computers[_rng.Next(s_computers.Length)],
            LogName = $"Channel{logIndex}",
            Keywords = s_keywords[_rng.Next(s_keywords.Length)],
            ActivityId = _allowNulls && _rng.Next(2) == 0 ? null : s_activityIds[_rng.Next(s_activityIds.Length)],
            ProcessId = _allowNulls && _rng.Next(2) == 0 ? null : 1 + _rng.Next(4),
            ThreadId = _allowNulls && _rng.Next(2) == 0 ? null : 1 + _rng.Next(4)
        });
    }
}
