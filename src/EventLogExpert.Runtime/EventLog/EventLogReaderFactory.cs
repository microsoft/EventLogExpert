// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Readers;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class EventLogReaderFactory : IEventLogReaderFactory
{
    public IEventLogReader CreateReader(string path, LogPathType pathType, bool renderXml = false, bool reverseDirection = false) =>
        new EventLogReader(path, pathType, renderXml, reverseDirection);
}
