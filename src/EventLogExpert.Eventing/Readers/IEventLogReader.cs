// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Readers;

/// <summary>
///     Reads event-log events in batches. Abstracts <see cref="EventLogReader" /> so the load pipeline can be driven
///     with a substitute reader under test. Only the members the pipeline consumes are exposed.
/// </summary>
public interface IEventLogReader : IDisposable
{
    /// <summary>
    ///     True when the underlying log opened successfully. False means the source could not be opened (the log does not
    ///     exist, access was denied, etc.); the load pipeline should surface a failure rather than an empty log.
    /// </summary>
    bool IsValid { get; }

    /// <summary>
    ///     The Win32 error from the most recent <see cref="TryGetEvents" /> that returned false, or
    ///     <see langword="null" /> when that false meant a clean end-of-results. A non-null value once the read loop ends
    ///     signals a failed read, not an empty log.
    /// </summary>
    int? LastErrorCode { get; }

    /// <summary>
    ///     The bookmark of the NEWEST event returned so far, irrespective of read direction. This is the correct resume
    ///     point for a live-tail watcher. <see langword="null" /> until the first event is returned.
    /// </summary>
    string? NewestBookmark { get; }

    /// <summary>
    ///     The Win32 error from a failed open, or <see langword="null" /> when <see cref="IsValid" />. Captured once at
    ///     open so it is stable (distinct from the per-read <see cref="LastErrorCode" />); use it to report why a log could
    ///     not be opened.
    /// </summary>
    int? OpenErrorCode { get; }

    /// <summary>
    ///     Reads the next batch of up to <paramref name="batchSize" /> events. Returns <see langword="false" /> when
    ///     there are no more events (or the source could not be opened), leaving <paramref name="events" /> empty.
    /// </summary>
    bool TryGetEvents(out EventRecord[] events, int batchSize = 30);
}
