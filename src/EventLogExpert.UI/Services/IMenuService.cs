// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Services;

/// <summary>
///     Coordinates a single active popup menu at a time. Hosts subscribe to <see cref="StateChanged" /> and render
///     <see cref="ActiveItems" /> at (<see cref="PositionX" />, <see cref="PositionY" />).
/// </summary>
public interface IMenuService
{
    /// <summary>
    ///     Raised when a top-level menu reports that the menubar should switch to the previous (-1) or next (+1) entry.
    ///     Menubars subscribe to coordinate ArrowLeft/ArrowRight navigation across dropdowns.
    /// </summary>
    event Action<int>? NavigateBarRequested;

    event Action? StateChanged;

    /// <summary>
    ///     Whether the host should refresh its captured "opener" element for this open. False is used when an existing
    ///     menu is being replaced and the original opener should be preserved (e.g., menubar arrow-key navigation).
    /// </summary>
    bool ActiveCaptureOpener { get; }

    /// <summary>Whether the next open should focus the first item (true) or the last (false).</summary>
    bool ActiveFocusFirst { get; }

    IReadOnlyList<MenuItem>? ActiveItems { get; }

    /// <summary>Per-open id; use as a <c>@key</c> so re-opening produces a fresh component instance.</summary>
    long ActiveMenuId { get; }

    double PositionX { get; }

    double PositionY { get; }

    /// <summary>Closes any active menu. No-op if none is open.</summary>
    void Close();

    /// <summary>Routes a menubar navigation request from a popup back to subscribed menubars.</summary>
    void NavigateBar(int direction);

    /// <summary>
    ///     Opens a menu at the given client-coordinate position. Replaces any previously active menu.
    ///     <paramref name="focusFirst" /> defaults to <c>true</c> for click/hover/ArrowDown opens; pass <c>false</c> for
    ///     ArrowUp opens so focus lands on the last enabled item per WAI-ARIA menubar pattern.
    ///     <paramref name="captureOpener" /> defaults to <c>true</c>; pass <c>false</c> when replacing an already-open
    ///     menu and the original opener should be preserved (e.g., menubar ArrowLeft/ArrowRight navigation), so closing
    ///     restores focus to the original menubar button instead of a transient menu item.
    /// </summary>
    void OpenAt(double x, double y, IReadOnlyList<MenuItem> items, bool focusFirst = true, bool captureOpener = true);
}
