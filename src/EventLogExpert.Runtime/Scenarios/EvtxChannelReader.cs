// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Readers;

namespace EventLogExpert.Runtime.Scenarios;

/// <summary>
///     Reads a file's channel with a single lightweight record read (System render only, no description resolution).
///     Fully non-throwing so one corrupt or locked file cannot fault a parallel folder scan.
/// </summary>
internal sealed class EvtxChannelReader : IEvtxChannelReader
{
    public EvtxChannelReadResult ReadChannel(string filePath)
    {
        try
        {
            using var reader = new EventLogReader(filePath, LogPathType.File, renderXml: false, reverseDirection: false);

            if (!reader.IsValid) { return EvtxChannelReadResult.Unreadable; }

            if (!reader.TryGetEvents(out var events, batchSize: 1))
            {
                // A false with no error code is a clean end-of-log (empty); a Win32 error is a read failure.
                return reader.LastErrorCode is null ? EvtxChannelReadResult.Empty : EvtxChannelReadResult.Unreadable;
            }

            if (events.Length == 0) { return EvtxChannelReadResult.Empty; }

            var record = events[0];

            return record.IsSuccess && !string.IsNullOrEmpty(record.LogName)
                ? EvtxChannelReadResult.FromChannel(record.LogName)
                : EvtxChannelReadResult.Unreadable;
        }
        catch (Exception)
        {
            return EvtxChannelReadResult.Unreadable;
        }
    }
}
