// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.OfflineImaging.Containment;

// Re-root foreign-image paths without host expansion; drop anything not safely image-relative.
internal sealed class OfflineImagePathMapper(OfflineImageRoot imageRoot, ITraceLogger? logger)
{
    public string? Map(string? registryPath)
    {
        if (string.IsNullOrWhiteSpace(registryPath)) { return null; }

        string value = registryPath.Trim().Trim('"').Trim();

        if (value.Length == 0) { return null; }

        // Reject UNC, DOS-device, extended-length, and NT object paths before normalization because they can escape the image.
        if (value.StartsWith(@"\\", StringComparison.Ordinal) || value.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            return Drop(registryPath, "UNC/DOS-device/NT path form");
        }

        string? relativeToImageRoot = ToImageRootRelative(value);

        if (relativeToImageRoot is null)
        {
            return Drop(registryPath, "unsupported path form");
        }

        // Unsupported variables and colon-bearing remnants fail closed instead of guessing host-specific paths or streams.
        if (relativeToImageRoot.Contains('%', StringComparison.Ordinal) ||
            relativeToImageRoot.Contains(':', StringComparison.Ordinal))
        {
            return Drop(registryPath, "residual environment token or stream/drive separator");
        }

        string mappedPath;

        try
        {
            mappedPath = Path.GetFullPath(Path.Combine(imageRoot.ImageRoot, relativeToImageRoot));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            // Hostile hive values can break GetFullPath; drop them so one bad value does not abort catalog reading.
            return Drop(registryPath, $"invalid path syntax ({ex.GetType().Name})");
        }

        // Normalize before containment so relative escapes are skipped instead of throwing out of enumeration.
        return !imageRoot.ContainsPath(mappedPath) ? Drop(registryPath, "path escapes the image root") : mappedPath;
    }

    private static bool IsDriveAbsolute(string value) =>
        value.Length >= 3 && IsDriveLetter(value[0]) && value[1] == ':' && value[2] is '\\' or '/';

    private static bool IsDriveLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    // Environment tokens are mapped without host expansion; bare or relative paths are anchored under image System32.
    private static string? ToImageRootRelative(string value)
    {
        if (TryStripTokenPrefix(value, "%SystemRoot%", out string remainder) ||
            TryStripTokenPrefix(value, "%windir%", out remainder))
        {
            return Path.Combine("Windows", remainder);
        }

        if (TryStripTokenPrefix(value, "%SystemDrive%", out remainder))
        {
            return remainder;
        }

        // Only machine-scoped program-directory tokens have stable image-relative defaults; per-user tokens are dropped.
        if (TryStripTokenPrefix(value, "%ProgramFiles(x86)%", out remainder))
        {
            return Path.Combine("Program Files (x86)", remainder);
        }

        if (TryStripTokenPrefix(value, "%ProgramFiles%", out remainder))
        {
            return Path.Combine("Program Files", remainder);
        }

        if (TryStripTokenPrefix(value, "%CommonProgramFiles(x86)%", out remainder))
        {
            return Path.Combine("Program Files (x86)", "Common Files", remainder);
        }

        if (TryStripTokenPrefix(value, "%CommonProgramFiles%", out remainder))
        {
            return Path.Combine("Program Files", "Common Files", remainder);
        }

        if (TryStripTokenPrefix(value, "%ProgramData%", out remainder))
        {
            return Path.Combine("ProgramData", remainder);
        }

        if (IsDriveAbsolute(value))
        {
            return value[3..];
        }

        // Drive-relative paths resolve against the host's per-drive current directory, so reject them.
        if (value.Length >= 2 && IsDriveLetter(value[0]) && value[1] == ':')
        {
            return null;
        }

        return value[0] is '\\' or '/' ? value.TrimStart('\\', '/') :
            Path.Combine("Windows", "System32", value);
    }

    private static bool TryStripTokenPrefix(string value, string token, out string remainder)
    {
        // Require a path boundary so "%ProgramFiles%evil" is not re-rooted under Program Files.
        if (value.StartsWith(token, StringComparison.OrdinalIgnoreCase) &&
            (value.Length == token.Length || value[token.Length] is '\\' or '/'))
        {
            remainder = value[token.Length..].TrimStart('\\', '/');

            return true;
        }

        remainder = string.Empty;

        return false;
    }

    private string? Drop(string registryPath, string reason)
    {
        logger?.Debug($"{nameof(OfflineImagePathMapper)}: dropping '{registryPath}' ({reason}); cannot map onto the image safely.");

        return null;
    }
}
