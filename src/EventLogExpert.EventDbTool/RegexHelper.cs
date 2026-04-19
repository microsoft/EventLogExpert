// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using System.Text.RegularExpressions;

namespace EventLogExpert.EventDbTool;

/// <summary>
///     Centralized creation of <see cref="Regex" /> instances for user-supplied <c>--filter</c> patterns.
///     Always sets a match timeout to bound worst-case execution against catastrophic backtracking, and
///     converts <see cref="ArgumentException" /> from invalid patterns into a logged error rather than
///     letting it terminate the process.
/// </summary>
internal static class RegexHelper
{
    /// <summary>Maximum time a single regex match is allowed to take before throwing.</summary>
    private static readonly TimeSpan s_matchTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Attempts to compile <paramref name="pattern" /> into a case-insensitive <see cref="Regex" /> with
    ///     a bounded match timeout. A null/empty pattern is treated as "no filter": <paramref name="regex" />
    ///     is set to <see langword="null" /> and the method still returns <see langword="true" /> so callers
    ///     can distinguish an absent filter from a malformed one.
    /// </summary>
    public static bool TryCreate(string? pattern, ITraceLogger logger, out Regex? regex)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            regex = null;
            return true;
        }

        try
        {
            regex = new Regex(pattern, RegexOptions.IgnoreCase, s_matchTimeout);
            return true;
        }
        catch (ArgumentException ex)
        {
            logger.Error($"Invalid --filter regex '{pattern}': {ex.Message}");
            regex = null;
            return false;
        }
    }
}
