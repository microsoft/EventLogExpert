// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterLibrary;

namespace EventLogExpert.UI.Common;

internal readonly record struct ComponentId
{
    private readonly string? _value;

    private ComponentId(string value)
    {
        if (!IsValidId(value))
        {
            throw new ArgumentException($"'{value}' is not a valid DOM id (expected {ValidIdPattern}).", nameof(value));
        }

        _value = value;
    }

    public const string ValidIdPattern = "^[A-Za-z][A-Za-z0-9_-]*$";

    public bool IsEmpty => _value is null;

    public string Value => _value ?? throw new InvalidOperationException("ComponentId is uninitialized.");

    public static ComponentId NewUnique() => NewUnique("cid");

    public static ComponentId NewUnique(string prefix) =>
        !IsValidId(prefix) ?
            throw new ArgumentException($"'{prefix}' is not a valid id prefix (expected {ValidIdPattern}).", nameof(prefix)) :
            new ComponentId($"{prefix}-{Guid.NewGuid():N}");

    public static ComponentId For(FilterId id, ComponentIdScope scope) =>
        id.Value == Guid.Empty ? throw new ArgumentException("Filter id must not be empty.", nameof(id)) :
            new ComponentId($"{Prefix(scope)}-{id.Value:N}");

    public static ComponentId For(LibraryEntryId id) =>
        id.Value == Guid.Empty ? throw new ArgumentException("Library entry id must not be empty.", nameof(id)) :
            new ComponentId($"le-{id.Value:N}");

    public ComponentId Suffix(string part) =>
        string.IsNullOrWhiteSpace(part) || !IsValidIdFragment(part) ?
            throw new ArgumentException($"'{part}' is not a valid id suffix (expected characters in [A-Za-z0-9_-]).", nameof(part)) :
            new ComponentId($"{Value}_{part}");

    public override string ToString() => _value ?? string.Empty;

    private static string Prefix(ComponentIdScope scope) => scope switch
    {
        ComponentIdScope.PaneFilter => "fp",
        ComponentIdScope.PanePendingFilter => "fp-pending",
        ComponentIdScope.LibraryFilter => "lef",
        ComponentIdScope.LibraryPendingFilter => "lef-pending",
        ComponentIdScope.Predicate => "sf",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown component id scope."),
    };

    private static bool IsValidId(string value) =>
        !string.IsNullOrEmpty(value) && char.IsAsciiLetter(value[0]) && IsValidIdFragment(value);

    private static bool IsValidIdFragment(string value)
    {
        foreach (char character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-'))
            {
                return false;
            }
        }

        return true;
    }
}
