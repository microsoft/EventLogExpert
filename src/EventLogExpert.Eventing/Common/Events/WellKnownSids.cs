// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Eventing.Common.Events;

internal static class WellKnownSids
{
    private static readonly Dictionary<string, string> s_namesBySid = new(StringComparer.OrdinalIgnoreCase)
    {
        ["S-1-0-0"] = "NULL SID",
        ["S-1-1-0"] = "Everyone",
        ["S-1-5-7"] = @"NT AUTHORITY\ANONYMOUS LOGON",
        ["S-1-5-11"] = @"NT AUTHORITY\Authenticated Users",
        ["S-1-5-18"] = @"NT AUTHORITY\SYSTEM",
        ["S-1-5-19"] = @"NT AUTHORITY\LOCAL SERVICE",
        ["S-1-5-20"] = @"NT AUTHORITY\NETWORK SERVICE",
        ["S-1-5-32-544"] = @"BUILTIN\Administrators",
        ["S-1-5-32-545"] = @"BUILTIN\Users",
        ["S-1-5-32-546"] = @"BUILTIN\Guests",
        ["S-1-5-32-547"] = @"BUILTIN\Power Users",
        ["S-1-5-32-555"] = @"BUILTIN\Remote Desktop Users",
        ["S-1-5-32-568"] = @"BUILTIN\IIS_IUSRS"
    };

    /// <summary>Returns the canonical display name for a well-known <paramref name="sid" /> (SDDL form), if known.</summary>
    internal static bool TryGetName(string sid, [NotNullWhen(true)] out string? name) => s_namesBySid.TryGetValue(sid, out name);
}
