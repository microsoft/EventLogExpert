// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Interfaces;

/// <summary>
///     Coordinates a single active popup menu at a time. Hosts subscribe to <see cref="StateChanged" /> and render
///     <see cref="ActiveItems" /> at (<see cref="PositionX" />, <see cref="PositionY" />).
/// </summary>
public interface IMenuService
{
    /// <summary>Raised when a top-level menu reports the menubar should switch to the previous (-1) or next (+1) entry.</summary>
    event Action<int>? NavigateBarRequested;

    event Action? StateChanged;

    /// <summary>
    ///     False when an existing menu is being replaced and the original opener should be preserved (e.g., menubar
    ///     arrow-key navigation).
    /// </summary>
    bool ActiveCaptureOpener { get; }

    /// <summary>True to focus the first item, false for the last (e.g., ArrowUp open).</summary>
    bool ActiveFocusFirst { get; }

    IReadOnlyList<MenuItem>? ActiveItems { get; }

    /// <summary>Per-open id; use as a <c>@key</c> so re-opening produces a fresh component instance.</summary>
    long ActiveMenuId { get; }

    double PositionX { get; }

    double PositionY { get; }

    void Close();

    void NavigateBar(int direction);

    /// <summary>
    ///     Opens a menu at the given client-coordinate position, replacing any active menu. Pass
    ///     <paramref name="focusFirst" /> false for ArrowUp opens (focus lands on the last enabled item per WAI-ARIA menubar
    ///     pattern). Pass <paramref name="captureOpener" /> false when replacing an open menu so closing restores focus to the
    ///     original opener.
    /// </summary>
    void OpenAt(double x, double y, IReadOnlyList<MenuItem> items, bool focusFirst = true, bool captureOpener = true);
}
