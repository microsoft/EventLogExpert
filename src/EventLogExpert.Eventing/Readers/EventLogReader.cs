// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.Structured;
using System.Buffers;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Readers;

public sealed class EventLogReader(
    string path,
    LogPathType pathType,
    bool renderXml = false,
    bool reverseDirection = false) : IEventLogReader
{
    private const int EvtQueryReverseDirection = 0x200;

    private readonly Lock _eventLock = new();

    private readonly EvtHandle _handle = reverseDirection
        ? NativeMethods.EvtQueryWithFlags(EventLogSession.GlobalSession.Handle, path, null,
            (int)pathType | EvtQueryReverseDirection)
        : NativeMethods.EvtQuery(EventLogSession.GlobalSession.Handle, path, null, pathType);

    private readonly int _openError = Marshal.GetLastWin32Error();

    private int _disposed;
    private bool _newestCaptured;
    private string? _newestReverseBookmark;

    /// <summary>
    ///     <see langword="true" /> when the underlying <c>EvtQuery</c> handle was opened successfully. When
    ///     <see langword="false" />, the path could not be opened (invalid log name, missing or corrupt .evtx file, access
    ///     denied, etc.) and <see cref="TryGetEvents" /> will not return events.
    /// </summary>
    public bool IsValid => _handle is { IsInvalid: false };

    public string? LastBookmark { get; private set; }

    /// <summary>
    ///     When <see cref="TryGetEvents" /> returns <see langword="false" /> due to a Win32 error other than
    ///     <c>ERROR_NO_MORE_ITEMS</c>, this property contains the Win32 error code. A value of <see langword="null" /> means
    ///     either no error occurred or the last failure was a normal end-of-results condition.
    /// </summary>
    public int? LastErrorCode { get; private set; }

    /// <summary>
    ///     The bookmark of the NEWEST event this reader has returned, irrespective of read direction. It is the correct
    ///     resume point for a live-tail watcher, because the genuinely new events are the ones created after the newest one
    ///     already loaded. For a forward (oldest-first) read this is the most recently returned event and aliases
    ///     <see cref="LastBookmark" />; for a reverse (newest-first) read it is the FIRST event returned and is captured once.
    ///     Unlike <see cref="LastBookmark" />, which is always the last event ENUMERATED (and therefore the OLDEST event under
    ///     a reverse read), this never points at the wrong end of the log.
    /// </summary>
    public string? NewestBookmark => reverseDirection ? _newestReverseBookmark : LastBookmark;

    /// <summary>
    ///     The Win32 error from a failed <c>EvtQuery</c> open, or <see langword="null" /> when the log opened (
    ///     <see cref="IsValid" /> is <see langword="true" />). Captured once at construction, so unlike the per-read
    ///     <see cref="LastErrorCode" /> it is stable and reflects the open failure rather than a later read.
    /// </summary>
    public int? OpenErrorCode => IsValid ? null : _openError;

    public void Dispose()
    {
        // Use Interlocked.CompareExchange for atomic check-and-set.
        // Only one thread will successfully change _disposed from 0 to 1.
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return; // Already disposed by another thread
        }

        if (_handle is { IsInvalid: false })
        {
            _handle.Dispose();
        }
    }

    // Pre-Windows 11, a batch being returned can be maximum of (2 MB of data, batchSize count of events).
    // If the requested number of events in batchSize exceeded 2 MB, the call failed.
    // With a maximum event size of 64 KB, the maximum batchSize that won't exceed the maximum buffer
    // size is 30 (32 minus some overhead; refer to MS-EVEN6 for details).
    // Windows 11 and later will stop filling out the buffer when the maximum size is reached, regardless
    // of whether the requested batchSize was reached (but it will not exceed the requested count). The 30
    // ceiling is a pre-Windows 11 constraint; on supported Windows 11+ deployments a larger batchSize is
    // safe and faster (EvtNext just returns fewer when the 2 MB buffer fills first), so the log-load path
    // requests more.
    public bool TryGetEvents(out EventRecord[] events, int batchSize = 30)
    {
        var buffer = ArrayPool<IntPtr>.Shared.Rent(batchSize);
        int count = 0;

        try
        {
            bool success = NativeMethods.EvtNext(_handle, batchSize, buffer, 0, 0, ref count);

            if (!success)
            {
                var error = Marshal.GetLastWin32Error();
                LastErrorCode = error != Win32ErrorCodes.ERROR_NO_MORE_ITEMS ? error : null;
                events = [];
                return false;
            }

            LastErrorCode = null;

            try
            {
                using (_eventLock.EnterScope())
                {
                    LastBookmark = CreateBookmark(new EvtHandle(buffer[count - 1], false));

                    if (reverseDirection && !_newestCaptured)
                    {
                        _newestReverseBookmark = CreateBookmark(new EvtHandle(buffer[0], false));
                        _newestCaptured = true;
                    }
                }

                events = new EventRecord[count];
            }
            catch
            {
                // Bookmark capture (or the result allocation) threw before the render loop disposed the
                // batch handles; close them here so a failed read never leaks up to batchSize native event handles.
                for (int i = 0; i < count; i++) { new EvtHandle(buffer[i]).Dispose(); }

                throw;
            }

            for (int i = 0; i < count; i++)
            {
                using var eventHandle = new EvtHandle(buffer[i]);

                try
                {
                    events[i] = NativeMethods.RenderEvent(eventHandle);
                    events[i].Properties = NativeMethods.RenderEventProperties(eventHandle);

                    bool needsUserData = events[i].Properties.Length == 0;

                    if (renderXml || needsUserData)
                    {
                        var xml = NativeMethods.RenderEventXml(eventHandle);

                        if (renderXml) { events[i].Xml = xml; }

                        if (needsUserData)
                        {
                            var (fields, incomplete) = UserDataValueExtractor.Extract(xml);
                            events[i].UserData = fields;
                            events[i].UserDataIncomplete = incomplete;
                        }
                    }
                }
                catch (Exception ex)
                {
                    events[i] = new EventRecord { RecordId = null, Error = ex.Message };
                }
                finally
                {
                    events[i].PathName = path;
                    events[i].LogPathType = pathType;
                }
            }

            return true;
        }
        finally
        {
            ArrayPool<IntPtr>.Shared.Return(buffer);
        }
    }

    private static string? CreateBookmark(EvtHandle eventHandle)
    {
        using EvtHandle handle = NativeMethods.EvtCreateBookmark(null);
        int error = Marshal.GetLastWin32Error();

        if (handle.IsInvalid)
        {
            NativeMethods.ThrowEventLogException(error);
        }

        bool success = NativeMethods.EvtUpdateBookmark(handle, eventHandle);
        error = Marshal.GetLastWin32Error();

        if (!success)
        {
            NativeMethods.ThrowEventLogException(error);
        }

        success = NativeMethods.EvtRender(
            EvtHandle.Zero,
            handle,
            EvtRenderFlags.Bookmark,
            0,
            IntPtr.Zero,
            out int bufferUsed,
            out int _);

        error = Marshal.GetLastWin32Error();

        if (!success && error != Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER)
        {
            NativeMethods.ThrowEventLogException(error);
        }

        Span<char> buffer = stackalloc char[bufferUsed / sizeof(char)];

        unsafe
        {
            fixed (char* bufferPtr = buffer)
            {
                success = NativeMethods.EvtRender(
                    EvtHandle.Zero,
                    handle,
                    EvtRenderFlags.Bookmark,
                    bufferUsed,
                    (IntPtr)bufferPtr,
                    out bufferUsed,
                    out int _);
            }
        }

        error = Marshal.GetLastWin32Error();

        if (!success)
        {
            NativeMethods.ThrowEventLogException(error);
        }

        return bufferUsed - 1 <= 0 ? null : new string(buffer[..^1]);
    }
}
