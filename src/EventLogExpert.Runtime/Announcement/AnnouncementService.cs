// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Runtime.Announcement;

public sealed class AnnouncementService : IAnnouncementService
{
    private readonly Lock _stateLock = new();
    private readonly ITraceLogger _traceLogger;

    private string _currentAnnouncement = string.Empty;
    private int _seq;

    public AnnouncementService(ITraceLogger traceLogger)
    {
        ArgumentNullException.ThrowIfNull(traceLogger);

        _traceLogger = traceLogger;
    }

    public event Action? StateChanged;

    public string CurrentAnnouncement
    {
        get { lock (_stateLock) { return _currentAnnouncement; } }
    }

    public void Announce(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_stateLock)
        {
            _seq++;
            // ZWS toggle on odd seq guarantees DOM text mutation between consecutive identical
            // announcements; SR live regions do not re-announce when the text node does not change.
            _currentAnnouncement = (_seq % 2 == 0) ? message : message + "\u200B";
        }

        RaiseStateChangedSafely();
    }

    private void RaiseStateChangedSafely()
    {
        var handler = StateChanged;

        if (handler is null) { return; }

        foreach (Delegate subscriber in handler.GetInvocationList())
        {
            try { ((Action)subscriber).Invoke(); }
            catch (Exception ex)
            {
                _traceLogger.Warning(
                    $"{nameof(AnnouncementService)}.{nameof(StateChanged)} subscriber threw: {ex}");
            }
        }
    }
}
