// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Services;

/// <summary>
///     Builds the message text shown when one or more logs the user just tried to open contained zero
///     events. Singular and plural cases use distinct phrasings.
/// </summary>
public static class EmptyLogAlertFormatter
{
    public static string BuildMessage(IReadOnlyList<string> displayNames)
    {
        ArgumentNullException.ThrowIfNull(displayNames);

        if (displayNames.Count == 0)
        {
            throw new ArgumentException("At least one display name is required.", nameof(displayNames));
        }

        if (displayNames.Count == 1)
        {
            return $"Log contains no events: {displayNames[0]}";
        }

        return $"{displayNames.Count} logs contained no events: {string.Join(", ", displayNames)}";
    }
}
