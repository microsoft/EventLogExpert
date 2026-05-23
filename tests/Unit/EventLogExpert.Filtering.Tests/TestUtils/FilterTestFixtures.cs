// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Common.Events;
using System.Security.Principal;
using static EventLogExpert.Filtering.TestUtils.Constants.FilterTestConstants;

namespace EventLogExpert.Filtering.Tests.TestUtils;

/// <summary>
///     Deterministic <see cref="ResolvedEvent" /> instances used by the parity and allocation tests. Each fixture
///     covers a distinct combination of populated / null nullable fields and Keywords cardinality so the parity matrix
///     observes both the "match" and the "no match" branches of every emitter shape.
/// </summary>
internal static class FilterTestFixtures
{
    public static readonly Guid FixedActivityId = new("11111111-2222-3333-4444-555555555555");
    public static readonly DateTime FixedTimestamp = new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public static readonly SecurityIdentifier LocalSystem = new(LocalSystemSddl);

    public static IReadOnlyList<ResolvedEvent> All =>
    [
        FullyPopulated,
        NoNullables,
        KernelPower,
        ApplicationError,
        WerSystemError,
        WithEscapes
    ];

    /// <summary>Source=Application Error (perf-snapshot fixture).</summary>
    public static ResolvedEvent ApplicationError { get; } = new("Application", LogPathType.Channel)
    {
        Id = 1000,
        Source = "Application Error",
        Level = EventLevelError,
        ComputerName = EventComputerServer01,
        Description = "Application crashed",
        LogName = "Application",
        TaskCategory = "(100)",
        Keywords = [],
        TimeCreated = FixedTimestamp,
        Xml = string.Empty
    };

    /// <summary>Source=TestSource Id=100 Level=Error, populated nullables, Keywords=[Audit, System].</summary>
    public static ResolvedEvent FullyPopulated { get; } = new("TestLog", LogPathType.Channel)
    {
        Id = 100,
        ProcessId = 4,
        ThreadId = 8,
        RecordId = 1234567890123L,
        ActivityId = FixedActivityId,
        UserId = LocalSystem,
        ComputerName = EventComputerServer01,
        Description = "An error occurred in the application",
        Level = EventLevelError,
        LogName = "Application",
        Source = EventSourceTestSource,
        TaskCategory = EventTaskCategorySystem,
        Keywords = [KeywordAudit, KeywordSystem],
        TimeCreated = FixedTimestamp,
        Xml = "<Event><Data>data inside</Data></Event>"
    };

    /// <summary>Source=Microsoft-Windows-Kernel-Power TaskCategory=DirtyTransition (perf-snapshot fixture).</summary>
    public static ResolvedEvent KernelPower { get; } = new("System", LogPathType.Channel)
    {
        Id = 41,
        Source = "Microsoft-Windows-Kernel-Power",
        TaskCategory = "DirtyTransition",
        Level = "Critical",
        ComputerName = EventComputerServer01,
        Description = "Kernel-power dirty shutdown",
        LogName = "System",
        Keywords = [],
        TimeCreated = FixedTimestamp,
        Xml = string.Empty
    };

    /// <summary>Source=OtherSource Id=200 Level=Information, all nullables null, Keywords empty.</summary>
    public static ResolvedEvent NoNullables { get; } = new("TestLog", LogPathType.Channel)
    {
        Id = 200,
        ProcessId = null,
        ThreadId = null,
        RecordId = null,
        ActivityId = null,
        UserId = null,
        ComputerName = "SERVER02",
        Description = "Operation completed successfully",
        Level = EventLevelInformation,
        LogName = "Application",
        Source = "OtherSource",
        TaskCategory = "Security",
        Keywords = [],
        TimeCreated = FixedTimestamp,
        Xml = string.Empty
    };

    /// <summary>Id=1001 Source=Microsoft-Windows-WER-SystemErrorReporting (perf-snapshot fixture).</summary>
    public static ResolvedEvent WerSystemError { get; } = new("Application", LogPathType.Channel)
    {
        Id = 1001,
        Source = "Microsoft-Windows-WER-SystemErrorReporting",
        Level = EventLevelError,
        ComputerName = EventComputerServer01,
        Description = "Fault bucket entry",
        LogName = "Application",
        TaskCategory = "None",
        Keywords = [],
        TimeCreated = FixedTimestamp,
        Xml = string.Empty
    };

    /// <summary>Description containing the literal escape characters used by the escape-sequence fixtures.</summary>
    public static ResolvedEvent WithEscapes { get; } = new("TestLog", LogPathType.Channel)
    {
        Id = 1,
        Source = "Esc",
        Level = "Verbose",
        Description = "a\\b",
        ComputerName = "ESC",
        LogName = "TestLog",
        TaskCategory = "Esc",
        Keywords = [],
        TimeCreated = FixedTimestamp,
        Xml = string.Empty
    };
}
