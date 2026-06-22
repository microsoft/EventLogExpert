// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Readers;
using EventLogExpert.Provider.Resolution;
using static EventLogExpert.Eventing.TestUtils.Constants.Constants;

namespace EventLogExpert.Eventing.TestUtils;

public static class EventUtils
{
    public static EventRecord CreateBasicEvent() =>
        new()
        {
            RecordId = 1,
            ProviderName = TestProviderName,
            Id = 1000,
            ComputerName = LocalComputer,
            LogName = ApplicationLogName,
            TimeCreated = DateTime.UtcNow,
            Level = 3,
            ProcessId = 1234,
            ThreadId = 5678
        };

    public static IEnumerable<EventRecord> CreateDifferentEvents() =>
    [
        new()
        {
            RecordId = 1,
            ProviderName = TestProviderName,
            Id = 1000,
            ComputerName = LocalComputer,
            LogName = ApplicationLogName,
            TimeCreated = DateTime.UtcNow,
            Level = 3,
            ProcessId = 1234,
            ThreadId = 5678
        },
        new()
        {
            RecordId = 1,
            ProviderName = TestProviderName,
            Id = 1001,
            ComputerName = RemoteComputer,
            LogName = SystemLogName,
            TimeCreated = DateTime.UtcNow,
            Level = 1,
            ProcessId = 1234,
            ThreadId = 5678
        }
    ];

    public static EventModel CreateEventModel(
        int id,
        string? description = null,
        byte version = 0,
        string? logName = null,
        IReadOnlyList<long>? keywords = null,
        string? template = null) =>
        new()
        {
            Id = id,
            Version = version,
            LogName = logName,
            Keywords = keywords?.ToArray() ?? [],
            Description = description,
            Template = template
        };

    // This event has a message in the legacy provider, but a task in the modern provider.
    public static EventRecord CreateExchangeEventRecord() =>
        new()
        {
            Id = 4114,
            Keywords = 36028797018963968,
            Level = 4,
            LogName = "Application",
            Properties = ["SERVER1", "4", "Lots of copy status text", "False"],
            ProviderName = "MSExchangeRepl",
            RecordId = 9518530,
            Task = 1,
            TimeCreated = new DateTime(2023, 1, 7, 10, 2, 0, DateTimeKind.Unspecified),
            ProcessId = 1234,
            ThreadId = 5678
        };

    public static ProviderDetails CreateExchangeProviderDetails() =>
        new()
        {
            Events = [],
            Keywords = new Dictionary<long, string>(),
            Messages =
            [
                new MessageModel
                {
                    LogLink = null,
                    ProviderName = "MSExchangeRepl",
                    RawId = 1074008082,
                    ShortId = 4114,
                    Tag = null,
                    Template = null,
                    Text = "Database redundancy health check passed.%nDatabase copy: %1%nRedundancy count: %2%nIsSuppressed: %4%n%nErrors:%n%3\r\n"
                }
            ],
            Opcodes = new Dictionary<int, string>(),
            ProviderName = "MSExchangeRepl",
            Tasks = new Dictionary<int, string>
            {
                { 1, "Service" },
                { 2, "Exchange VSS Writer" },
                { 3, "Move" },
                { 4, "Upgrade" },
                { 5, "Action" },
                { 6, "ExRes" }
            }
        };

    public static MessageModel CreateMessageModel(
        string providerName,
        long rawId,
        string text,
        short? shortId = null,
        string? logLink = null,
        string? tag = null,
        string? template = null) =>
        new()
        {
            LogLink = logLink,
            ProviderName = providerName,
            RawId = rawId,
            ShortId = shortId ?? 0,
            Tag = tag,
            Template = template,
            Text = text
        };

    public static ProviderDetails CreateProvider(
        string name,
        IReadOnlyList<MessageModel>? messages = null,
        IReadOnlyList<EventModel>? events = null,
        IDictionary<long, string>? keywords = null,
        IDictionary<int, string>? opcodes = null,
        IDictionary<int, string>? tasks = null,
        string? resolvedFromOwningPublisher = null) =>
        new()
        {
            ProviderName = name,
            Messages = messages ?? [],
            Parameters = [],
            Events = events ?? [],
            Keywords = keywords ?? new Dictionary<long, string>(),
            Opcodes = opcodes ?? new Dictionary<int, string>(),
            Tasks = tasks ?? new Dictionary<int, string>(),
            ResolvedFromOwningPublisher = resolvedFromOwningPublisher
        };

    /// <summary>Creates a modern event with a template and description for property resolution tests.</summary>
    internal static (ProviderDetails Details, EventRecord Record) CreateModernEvent(
        string description,
        string template,
        IReadOnlyList<EventProperty> properties,
        ushort id = 1000,
        byte version = 0) =>
    (
        new ProviderDetails
        {
            ProviderName = TestProviderName,
            Events =
            [
                new EventModel
                {
                    Id = id,
                    Version = version,
                    Keywords = [],
                    LogName = ApplicationLogName,
                    Description = description,
                    Template = template
                }
            ],
            Messages = [],
            Parameters = [],
            Keywords = new Dictionary<long, string>(),
            Tasks = new Dictionary<int, string>()
        },
        new EventRecord
        {
            ProviderName = TestProviderName,
            Id = id,
            Version = version,
            LogName = ApplicationLogName,
            Properties = properties
        }
    );
}
