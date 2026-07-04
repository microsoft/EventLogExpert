// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.OfflineImaging.Containment;

// The image root, not the host volume root, is the re-root base and containment boundary.
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

    public string ImageRoot { get; }

    public string SoftwareHivePath { get; }

    public string System32Directory { get; }

    public string SystemHivePath { get; }

    public string WindowsDirectory { get; }

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
            // Canonicalize the root once so later reparse-resolved paths compare against the same containment base.
            full = ResolveReparsePoints(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PathTooLongException or NotSupportedException)
        {
            logger?.Debug($"{nameof(OfflineImageRoot)}: could not resolve reparse points for image path '{imageOrWindowsRoot}': {ex.Message}");

            return null;
        }

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

    // Trailing separators make the lexical containment check segment-boundary safe.
    public bool ContainsPath(string fullPath) =>
        EnsureTrailingSeparator(Path.GetFullPath(fullPath)).StartsWith(_imageRootWithSeparator, StringComparison.OrdinalIgnoreCase);

    // Resolve each reparse point before containment so junctions at any level cannot hide an escape.
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
