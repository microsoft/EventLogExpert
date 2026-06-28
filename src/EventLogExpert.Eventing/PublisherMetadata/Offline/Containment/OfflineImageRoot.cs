// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline.Containment;

/// <summary>
///     Describes a mounted or extracted foreign Windows image: the image root (the directory that contains
///     <c>Windows</c>), the image's <c>Windows</c> / <c>System32</c> directories, and the on-disk <c>SOFTWARE</c> /
///     <c>SYSTEM</c> hive files. The image root - NOT the host volume root - is the re-rooting base and the root-guard
///     boundary, so an extracted image under <c>D:\Win2019\Windows</c> is bounded by <c>D:\Win2019</c> (a host
///     <c>C:\Windows\…</c> path then correctly fails the guard) while a mounted volume <c>X:\Windows</c> is bounded by
///     <c>X:\</c>.
/// </summary>
internal sealed class OfflineImageRoot
{
    private readonly string _imageRootWithSeparator;

    private OfflineImageRoot(string imageRoot, string windowsDirectory, string softwareHivePath, string systemHivePath)
    {
        ImageRoot = imageRoot;
        WindowsDirectory = windowsDirectory;
        System32Directory = Path.Combine(windowsDirectory, "System32");
        SoftwareHivePath = softwareHivePath;
        SystemHivePath = systemHivePath;
        _imageRootWithSeparator = EnsureTrailingSeparator(imageRoot);
    }

    /// <summary>The directory that contains the image's <c>Windows</c> folder; the re-root base and root-guard boundary.</summary>
    public string ImageRoot { get; }

    /// <summary>Full path to the image's <c>SOFTWARE</c> hive file.</summary>
    public string SoftwareHivePath { get; }

    /// <summary>The image's <c>Windows\System32</c> directory.</summary>
    public string System32Directory { get; }

    /// <summary>Full path to the image's <c>SYSTEM</c> hive file.</summary>
    public string SystemHivePath { get; }

    /// <summary>The image's <c>Windows</c> directory (e.g. <c>X:\Windows</c>).</summary>
    public string WindowsDirectory { get; }

    /// <summary>
    ///     Accepts either the image root (the directory containing <c>Windows</c>, e.g. a mounted volume root <c>X:\</c>
    ///     or an extracted <c>D:\Win2019</c>) or the image's <c>Windows</c> directory itself, and resolves the layout. Returns
    ///     <see langword="null" /> unless BOTH the <c>SOFTWARE</c> and <c>SYSTEM</c> hives are present (validating up front
    ///     avoids starting a run that can only ever drop legacy providers). A malformed path is also a <see langword="null" />
    ///     (fail-closed) result rather than a throw, so a hostile or unreadable image path is skipped, not surfaced as an
    ///     exception from the public offline-source enumeration.
    /// </summary>
    public static OfflineImageRoot? TryCreate(string imageOrWindowsRoot, ITraceLogger? logger)
    {
        if (string.IsNullOrWhiteSpace(imageOrWindowsRoot))
        {
            logger?.Debug($"{nameof(OfflineImageRoot)}: image path was null or empty.");

            return null;
        }

        string full;

        try
        {
            full = Path.GetFullPath(imageOrWindowsRoot.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            logger?.Debug($"{nameof(OfflineImageRoot)}: image path '{imageOrWindowsRoot}' is not a valid path: {ex.Message}");

            return null;
        }

        try
        {
            // Canonicalize the root's OWN reparse points up front so the boundary is comparable like-for-like with the
            // reparse-resolved file paths the guard checks: if the image root is itself reached through a junction / folder
            // mount-point, a lexical boundary would make every resolved file path fail containment. Resolving once here
            // keeps the whole layout (Windows / System32 / hives / re-rooted paths) on one canonical base.
            full = ResolveReparsePoints(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PathTooLongException or NotSupportedException)
        {
            logger?.Debug($"{nameof(OfflineImageRoot)}: could not resolve reparse points for image path '{imageOrWindowsRoot}': {ex.Message}");

            return null;
        }

        // The input is the Windows directory itself when its System32\config\SOFTWARE exists directly under it;
        // otherwise treat the input as the image root and look for a Windows subdirectory.
        string windowsDirectory = HiveExistsUnderWindows(full) ? full : Path.Combine(full, "Windows");

        string softwareHivePath = HivePath(windowsDirectory, "SOFTWARE");
        string systemHivePath = HivePath(windowsDirectory, "SYSTEM");

        if (!File.Exists(softwareHivePath))
        {
            logger?.Debug($"{nameof(OfflineImageRoot)}: SOFTWARE hive not found at {softwareHivePath} for input {imageOrWindowsRoot}.");

            return null;
        }

        if (!File.Exists(systemHivePath))
        {
            logger?.Debug($"{nameof(OfflineImageRoot)}: SYSTEM hive not found at {systemHivePath} for input {imageOrWindowsRoot}.");

            return null;
        }

        string? imageRoot = Path.GetDirectoryName(windowsDirectory);

        if (string.IsNullOrEmpty(imageRoot))
        {
            logger?.Debug($"{nameof(OfflineImageRoot)}: could not determine the image root (parent of {windowsDirectory}).");

            return null;
        }

        return new OfflineImageRoot(imageRoot, windowsDirectory, softwareHivePath, systemHivePath);
    }

    /// <summary>
    ///     True when <paramref name="fullPath" /> lies under the image root. Segment-boundary safe (a trailing separator
    ///     is appended to both sides, so a sibling like <c>X:\WindowsEvil</c> never matches an image root of <c>X:\Windows</c>
    ///     ) and case-insensitive. Purely lexical: callers that must honor reparse points (<see cref="OfflineRootGuard" /> for
    ///     guarded file paths, and <see cref="TryCreate" /> for the root boundary itself) canonicalize via
    ///     <see cref="ResolveReparsePoints" /> first so both sides of the comparison share one reparse-resolved base.
    /// </summary>
    public bool ContainsPath(string fullPath) =>
        EnsureTrailingSeparator(Path.GetFullPath(fullPath)).StartsWith(_imageRootWithSeparator, StringComparison.OrdinalIgnoreCase);

    // Walks fullPath component-by-component from its root, replacing the first existing component that is a reparse point
    // with its final link target and continuing from there, so a junction at ANY level - including the image root itself
    // reached via a folder mount-point - is canonicalized before the containment comparison. Shared by TryCreate (the root
    // boundary) and OfflineRootGuard (every guarded file path) so the two sides compare like-for-like. A resolution failure
    // propagates; each caller decides whether that is fail-closed-null (TryCreate) or a guard violation (OfflineRootGuard).
    internal static string ResolveReparsePoints(string fullPath)
    {
        string root = Path.GetPathRoot(fullPath) ?? string.Empty;

        if (root.Length == 0) { return fullPath; }

        string resolved = root;
        string[] segments = fullPath[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string segment in segments)
        {
            string candidate = Path.Combine(resolved, segment);

            if ((File.Exists(candidate) || Directory.Exists(candidate)) &&
                (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0 &&
                File.ResolveLinkTarget(candidate, returnFinalTarget: true) is { } target)
            {
                resolved = target.FullName;

                continue;
            }

            resolved = candidate;
        }

        return resolved;
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    private static bool HiveExistsUnderWindows(string windowsDirectory) => File.Exists(HivePath(windowsDirectory, "SOFTWARE"));

    private static string HivePath(string windowsDirectory, string hiveName) =>
        Path.Combine(windowsDirectory, "System32", "config", hiveName);
}
