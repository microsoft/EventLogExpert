// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

/// <summary>
///     Immutable description of a single entry in a menu (menu bar dropdown, context menu, or submenu).
///     Used by the data-driven MenuRenderer; pair instances via <see cref="Children"/> or
///     <see cref="ChildrenLoader"/> to build trees.
/// </summary>
public sealed record MenuItem
{
    public string Label { get; init; } = string.Empty;

    public string? Shortcut { get; init; }

    public string? IconClass { get; init; }

    public Func<Task>? OnClickAsync { get; init; }

    public IReadOnlyList<MenuItem>? Children { get; init; }

    public Func<Task<IReadOnlyList<MenuItem>>>? ChildrenLoader { get; init; }

    /// <summary>
    ///     <c>null</c> for non-checkable items; <c>true</c>/<c>false</c> for checkable items so screen
    ///     readers can announce the toggle state via <c>role="menuitemcheckbox"</c> + <c>aria-checked</c>.
    /// </summary>
    public bool? IsChecked { get; init; }

    public bool IsSeparator { get; init; }

    public bool IsEnabled { get; init; } = true;

    public bool IsDanger { get; init; }

    public static MenuItem Separator() => new() { IsSeparator = true };

    public static MenuItem Item(
        string label,
        Func<Task> onClickAsync,
        string? shortcut = null,
        bool? isChecked = null,
        bool isEnabled = true,
        bool isDanger = false) =>
        new()
        {
            Label = label,
            OnClickAsync = onClickAsync,
            Shortcut = shortcut,
            IsChecked = isChecked,
            IsEnabled = isEnabled,
            IsDanger = isDanger,
        };

    public static MenuItem Item(
        string label,
        Action onClick,
        string? shortcut = null,
        bool? isChecked = null,
        bool isEnabled = true) =>
        Item(label, () => { onClick(); return Task.CompletedTask; }, shortcut, isChecked, isEnabled);

    public static MenuItem SubMenu(string label, IReadOnlyList<MenuItem> children, bool isEnabled = true) =>
        new() { Label = label, Children = children, IsEnabled = isEnabled };

    public static MenuItem AsyncSubMenu(
        string label,
        Func<Task<IReadOnlyList<MenuItem>>> loader,
        bool isEnabled = true) =>
        new() { Label = label, ChildrenLoader = loader, IsEnabled = isEnabled };
}
