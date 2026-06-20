// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable;

public readonly record struct LogTabGroupId(Guid Value)
{
    // The implicit, always-present "all open logs" combined group. Distinct from any Create()d id
    // because Guid.NewGuid never returns Guid.Empty.
    public static LogTabGroupId AllLogs { get; } = new(Guid.Empty);

    public static LogTabGroupId Create() => new(Guid.NewGuid());

    public bool IsAll => Value == Guid.Empty;
}
