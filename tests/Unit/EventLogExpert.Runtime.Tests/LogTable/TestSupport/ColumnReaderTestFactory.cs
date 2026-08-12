// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.Tests.LogTable.TestSupport;

internal static class ColumnReaderTestFactory
{
    internal static IEventColumnReader ReaderOver(IReadOnlyList<ResolvedEvent> sample) =>
        EventColumnStore.Build([.. sample], generation: 1, contentVersion: 1).CreateReader(EventLogId.Create());
}
