// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Menu;

public sealed record MenuItem
{
    public string Label { get; init; } = string.Empty;

    public string? Shortcut { get; init; }

    public string? IconClass { get; init; }

    public Func<Task>? OnClickAsync { get; init; }

    public IReadOnlyList<MenuItem>? Children { get; init; }

    public Func<Task<IReadOnlyList<MenuItem>>>? ChildrenLoader { get; init; }

    public bool? IsChecked { get; init; }

    public bool IsSeparator { get; init; }

    public bool IsEnabled { get; init; } = true;

    public bool IsDanger { get; init; }

    public string? DisabledReason { get; init; }

    public string? StatusText { get; init; }

    public bool IsFocusable => !IsSeparator && (IsEnabled || DisabledReason is not null);

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
            IsEnabled = isEnabled,
            IsDanger = isDanger,
            DisabledReason = disabledReason,
            StatusText = statusText,
        };

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
