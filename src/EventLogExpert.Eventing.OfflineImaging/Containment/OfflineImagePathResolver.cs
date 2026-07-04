// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.OfflineImaging.Containment;

// Map first, then guard, so offline registry paths never reach host filesystem unchecked.
internal sealed class OfflineImagePathResolver(OfflineImagePathMapper mapper, OfflineRootGuard guard)
{
    public string? Resolve(string? registryValue, string what)
    {
        string? reRooted = mapper.Map(registryValue);

        if (reRooted is null) { return null; }

        try
        {
            guard.Assert(reRooted, what);
        }
        catch (OfflineRootGuardViolationException)
        {
            // Reparse-point escapes are hostile image content; drop them so one bad value cannot abort enumeration.
            return null;
        }

        return reRooted;
    }

    public IReadOnlyList<string> ResolveMany(string? multiValue, string what)
    {
        if (string.IsNullOrWhiteSpace(multiValue)) { return []; }

        var result = new List<string>();

        foreach (string part in multiValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Resolve(part, what) is { } resolved) { result.Add(resolved); }
        }

        return result;
    }
}
