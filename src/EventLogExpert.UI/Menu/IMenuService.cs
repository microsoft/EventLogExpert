// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Menu;

public interface IMenuService
{
    event Action<int>? NavigateBarRequested;

    event Action? StateChanged;

    bool ActiveCaptureOpener { get; }

    bool ActiveFocusFirst { get; }

    IReadOnlyList<MenuItem>? ActiveItems { get; }

    long ActiveMenuId { get; }

    double PositionX { get; }

    double PositionY { get; }

    void Close();

    void NavigateBar(int direction);

    void OpenAt(double x, double y, IReadOnlyList<MenuItem> items, bool focusFirst = true, bool captureOpener = true);
}
