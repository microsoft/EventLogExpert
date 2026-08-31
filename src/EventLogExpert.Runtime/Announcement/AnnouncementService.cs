// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.FilterLenses;

namespace EventLogExpert.Runtime.Announcement;

public sealed class AnnouncementService : IAnnouncementService
{
    private readonly Lock _stateLock = new();
    private readonly ITraceLogger _traceLogger;

    private CurrentAnnouncement _current = new(new Announcement.Text(string.Empty), 0);

    public AnnouncementService(ITraceLogger traceLogger)
    {
        ArgumentNullException.ThrowIfNull(traceLogger);

        _traceLogger = traceLogger;
    }

    public event Action? StateChanged;

    public CurrentAnnouncement Current
    {
        get { lock (_stateLock) { return _current; } }
    }

    public void Announce(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        Publish(new Announcement.Text(message));
    }

    public void AnnounceLensKept(FilterLensLabel label)
    {
        ArgumentNullException.ThrowIfNull(label);

        Publish(new Announcement.LensKept(label));
    }

    private void Publish(Announcement payload)
    {
        lock (_stateLock)
        {
            // Monotonic seq drives the host's re-announce toggle; the zero-width-space DOM mutation now lives in
            // AnnouncerHost (after localization), keeping it transparent to every plain-string caller.
            _current = new CurrentAnnouncement(payload, _current.Sequence + 1);
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
