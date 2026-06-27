// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.PublisherMetadata.Offline.Containment;

/// <summary>
///     The single seam every offline registry reader uses to turn a stored path into one that is safe to open: map it
///     onto the image (<see cref="OfflineImagePathMapper" />), then assert it stays inside the image (
///     <see cref="OfflineRootGuard" />). Keeping the map-then-guard contract here means callers never see an unsafe path.
/// </summary>
internal sealed class OfflineImagePathResolver(OfflineImagePathMapper mapper, OfflineRootGuard guard)
{
    /// <summary>
    ///     Maps a single registry value onto the image, or <see langword="null" /> when it cannot be mapped safely.
    ///     Throws <see cref="OfflineRootGuardViolationException" /> if a mapped path escapes the image (a contamination bug,
    ///     not a recoverable condition).
    /// </summary>
    public string? Resolve(string? registryValue, string what)
    {
        string? reRooted = mapper.Map(registryValue);

        if (reRooted is null) { return null; }

        guard.Assert(reRooted, what);

        return reRooted;
    }

    /// <summary>
    ///     Resolves each segment of a multi-valued (<c>;</c>-separated) registry path string, preserving order and
    ///     dropping any that cannot be mapped safely.
    /// </summary>
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
