// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace EventLogExpert.DatabaseTools.Common;

/// <summary>
///     Centralized creation of <see cref="Regex" /> instances for user-supplied filter patterns. Always sets a match
///     timeout to bound worst-case execution against catastrophic backtracking, and converts the
///     <see cref="ArgumentException" /> raised by malformed patterns into an out-string error so callers can surface it in
///     their own presentation context (CLI logger, UI banner, etc.) without coupling the factory to any specific surface.
/// </summary>
public static class FilterRegexFactory
{
    private static readonly TimeSpan s_matchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Attempts to compile <paramref name="pattern" /> into a case-insensitive <see cref="Regex" /> with a bounded
    ///     match timeout. A null/empty pattern is treated as "no filter": <paramref name="regex" /> and
    ///     <paramref name="error" /> are both set to <see langword="null" /> and the method returns <see langword="true" /> so
    ///     callers can distinguish an absent filter from a malformed one. On parse failure both <paramref name="regex" /> is
    ///     null and <paramref name="error" /> holds <see cref="Exception.Message" /> from the <see cref="ArgumentException" />
    ///     raised by <see cref="Regex(string,RegexOptions,TimeSpan)" />.
    /// </summary>
    public static bool TryCreate(string? pattern, out Regex? regex, out string? error)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            regex = null;
            error = null;

            return true;
        }

        try
        {
            regex = new Regex(pattern, RegexOptions.IgnoreCase, s_matchTimeout);
            error = null;

            return true;
        }
        catch (ArgumentException ex)
        {
            regex = null;
            error = ex.Message;

            return false;
        }
    }
}
