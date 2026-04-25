// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Services;

public sealed class MenuService : IMenuService
{
    private readonly Lock _stateLock = new();

    private long _idCounter;

    public event Action<int>? NavigateBarRequested;

    public event Action? StateChanged;

    public bool ActiveCaptureOpener { get; private set; } = true;

    public bool ActiveFocusFirst { get; private set; } = true;

    public IReadOnlyList<MenuItem>? ActiveItems { get; private set; }

    public long ActiveMenuId { get; private set; }

    public double PositionX { get; private set; }

    public double PositionY { get; private set; }

    public void Close()
    {
        bool changed;

        lock (_stateLock)
        {
            changed = ActiveMenuId != 0;
            ActiveMenuId = 0;
            ActiveItems = null;
            PositionX = 0;
            PositionY = 0;
            ActiveFocusFirst = true;
            ActiveCaptureOpener = true;
        }

        if (changed) { StateChanged?.Invoke(); }
    }

    public void NavigateBar(int direction) => NavigateBarRequested?.Invoke(direction);

    public void OpenAt(double x, double y, IReadOnlyList<MenuItem> items, bool focusFirst = true, bool captureOpener = true)
    {
        ArgumentNullException.ThrowIfNull(items);

        lock (_stateLock)
        {
            _idCounter++;
            ActiveMenuId = _idCounter;
            ActiveItems = items;
            PositionX = x;
            PositionY = y;
            ActiveFocusFirst = focusFirst;
            ActiveCaptureOpener = captureOpener;
        }

        StateChanged?.Invoke();
    }
}
