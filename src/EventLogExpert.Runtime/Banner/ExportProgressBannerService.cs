// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Banner;

internal sealed class ExportProgressBannerService : IExportProgressBannerService
{
    private readonly Lock _stateLock = new();

    private ExportProgressEntry? _currentExport;

    public event Action? StateChanged;

    public ExportProgressEntry? CurrentExport
    {
        get { using (_stateLock.EnterScope()) { return _currentExport; } }
    }

    public void Begin(string message, Action cancel)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);
        ArgumentNullException.ThrowIfNull(cancel);

        using (_stateLock.EnterScope())
        {
            _currentExport = new ExportProgressEntry(message, cancel);
        }

        // Raised outside the lock so a subscriber re-reading CurrentExport cannot deadlock.
        StateChanged?.Invoke();
    }

    public void End()
    {
        using (_stateLock.EnterScope())
        {
            if (_currentExport is null) { return; }

            _currentExport = null;
        }

        StateChanged?.Invoke();
    }
}
