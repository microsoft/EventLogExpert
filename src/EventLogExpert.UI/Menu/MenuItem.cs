// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Menu;

public enum MenuItemSelection
{
    None,
    Checkbox,
    Radio
}

public sealed record MenuItem
{
    public string Label { get; init; } = string.Empty;

    public string? Shortcut { get; init; }

    public string? IconClass { get; init; }

    public Func<Task>? OnClickAsync { get; init; }

    public IReadOnlyList<MenuItem>? Children { get; init; }

    public Func<Task<IReadOnlyList<MenuItem>>>? ChildrenLoader { get; init; }

    public bool? IsChecked { get; init; }

    // Distinguishes a mutually-exclusive radio item from a toggle checkbox; the renderer maps it to the ARIA role.
    public MenuItemSelection Selection { get; init; } = MenuItemSelection.None;

    public bool IsSeparator { get; init; }

    public bool IsEnabled { get; init; } = true;

    public bool IsDanger { get; init; }

    public string? DisabledReason { get; init; }

    public string? StatusText { get; init; }

    public bool IsFocusable => !IsSeparator && IsEnabled;

    public static MenuItem Separator() => new() { IsSeparator = true };

    public static MenuItem Item(
        string label,
        Func<Task> onClickAsync,
        string? shortcut = null,
        bool? isChecked = null,
        bool isEnabled = true,
        bool isDanger = false,
        string? disabledReason = null,
        string? statusText = null) =>
        new()
        {
            Label = label,
            OnClickAsync = onClickAsync,
            Shortcut = shortcut,
            IsChecked = isChecked,
            Selection = isChecked is null ? MenuItemSelection.None : MenuItemSelection.Checkbox,
            IsEnabled = isEnabled,
            IsDanger = isDanger,
            DisabledReason = disabledReason,
            StatusText = statusText,
        };

    // A mutually-exclusive selection within a group (Order By / Group By columns), rendered as role="menuitemradio".
    public static MenuItem Radio(
        string label,
        Func<Task> onClickAsync,
        bool isChecked,
        bool isEnabled = true) =>
        new()
        {
            Label = label,
            OnClickAsync = onClickAsync,
            IsChecked = isChecked,
            Selection = MenuItemSelection.Radio,
            IsEnabled = isEnabled,
        };

    public static MenuItem Radio(
        string label,
        Action onClick,
        bool isChecked,
        bool isEnabled = true) =>
        Radio(label, () => { onClick(); return Task.CompletedTask; }, isChecked, isEnabled);

    public static MenuItem Item(
        string label,
        Action onClick,
        string? shortcut = null,
        bool? isChecked = null,
        bool isEnabled = true,
        string? disabledReason = null,
        string? statusText = null) =>
        Item(
            label,
            () => { onClick(); return Task.CompletedTask; },
            shortcut,
            isChecked,
            isEnabled,
            isDanger: false,
            disabledReason,
            statusText);

    public static MenuItem SubMenu(string label, IReadOnlyList<MenuItem> children, bool isEnabled = true) =>
        new() { Label = label, Children = children, IsEnabled = isEnabled };

    public static MenuItem AsyncSubMenu(
        string label,
        Func<Task<IReadOnlyList<MenuItem>>> loader,
        bool isEnabled = true) =>
        new() { Label = label, ChildrenLoader = loader, IsEnabled = isEnabled };
}
