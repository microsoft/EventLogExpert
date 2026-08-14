// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.Readers;
using System.Buffers;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Resolvers;

/// <summary>
///     Renders event XML on demand by scanning a log once with <c>EvtQuery</c> and rendering only the records a
///     caller selects, so an XML-referencing filter can be evaluated without reopening every log with pre-rendered XML.
/// </summary>
public sealed class EventXmlBatchScanner : IEventXmlBatchScanner
{
    private const string AllEventsQuery = "*";

    // Largest EvtNext count that cannot overflow the pre-Windows 11 2 MB batch buffer (64 KB max event); matches EventLogReader.
    private const int DefaultBatchSize = 30;

    private readonly int _batchSize;

    public EventXmlBatchScanner() : this(DefaultBatchSize) { }

    internal EventXmlBatchScanner(int batchSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        _batchSize = batchSize;
    }

    public IEnumerable<ScannedEventXml> Scan(
        string owningLog,
        LogPathType pathType,
        Func<long, bool> shouldRenderXml,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(owningLog);
        ArgumentNullException.ThrowIfNull(shouldRenderXml);

        return ScanCore(owningLog, pathType, shouldRenderXml, cancellationToken);
    }

    private List<ScannedEventXml> RenderNextBatch(
        EvtHandle query,
        IntPtr[] buffer,
        Func<long, bool> shouldRenderXml,
        out bool endOfResults)
    {
        int returned = 0;
        bool success = NativeMethods.EvtNext(query, _batchSize, buffer, 0, 0, ref returned);

        if (!success)
        {
            int error = Marshal.GetLastWin32Error();

            // ERROR_NO_MORE_ITEMS is the normal end of the result set; any other failure truncated the scan and must
            // fault so a partial - and therefore wrong - membership set is never derived from it.
            if (error != Win32ErrorCodes.ERROR_NO_MORE_ITEMS) { NativeMethods.ThrowEventLogException(error); }

            endOfResults = true;

            return [];
        }

        endOfResults = returned == 0;

        if (returned == 0) { return []; }

        // Wrap every returned handle up front so a render throw part-way through still disposes the whole batch.
        EvtHandle[] eventHandles = new EvtHandle[returned];

        try
        {
            for (int index = 0; index < returned; index++)
            {
                eventHandles[index] = new EvtHandle(buffer[index]);
            }

            List<ScannedEventXml> results = new(returned);

            for (int index = 0; index < returned; index++)
            {
                EvtHandle eventHandle = eventHandles[index];

                if (eventHandle.IsInvalid) { continue; }

                if (NativeMethods.RenderEvent(eventHandle).RecordId is not { } recordId) { continue; }

                if (!shouldRenderXml(recordId)) { continue; }

                results.Add(new ScannedEventXml(recordId, NativeMethods.RenderEventXml(eventHandle) ?? string.Empty));
            }

            return results;
        }
        finally
        {
            foreach (EvtHandle handle in eventHandles) { handle?.Dispose(); }
        }
    }

    private IEnumerable<ScannedEventXml> ScanCore(
        string owningLog,
        LogPathType pathType,
        Func<long, bool> shouldRenderXml,
        CancellationToken cancellationToken)
    {
        using EvtHandle query = NativeMethods.EvtQuery(
            EventLogSession.GlobalSession.Handle,
            owningLog,
            AllEventsQuery,
            pathType);

        int openError = Marshal.GetLastWin32Error();

        if (query.IsInvalid) { NativeMethods.ThrowEventLogException(openError); }

        IntPtr[] buffer = ArrayPool<IntPtr>.Shared.Rent(_batchSize);

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                List<ScannedEventXml> batch = RenderNextBatch(query, buffer, shouldRenderXml, out bool endOfResults);

                foreach (ScannedEventXml scanned in batch) { yield return scanned; }

                if (endOfResults) { yield break; }
            }
        }
        finally
        {
            ArrayPool<IntPtr>.Shared.Return(buffer);
        }
    }
}
