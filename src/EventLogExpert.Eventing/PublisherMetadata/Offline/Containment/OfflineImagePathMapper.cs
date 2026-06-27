// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline.Containment;

/// <summary>
///     Maps a registry-stored DLL path (resource / message / parameter file) from a foreign Windows image onto the
///     mounted-or-extracted image root (re-rooting it), NEVER touching the host filesystem. Real images store these paths
///     predominantly as drive-absolute literals such as <c>C:\Windows\System32\foo.dll</c> (a host-registry sample found
///     842 drive-absolute vs 0 <c>%SystemRoot%</c> publisher paths), so absolute re-rooting - not env-token expansion - is
///     the load-bearing case, and the image's system drive may not be <c>C:</c>. The host
///     <see cref="Environment.ExpandEnvironmentVariables" /> is intentionally never used. Values that cannot be re-rooted
///     onto the image with certainty (unsupported environment tokens, UNC / DOS-device / NT / drive-relative forms,
///     alternate-data-stream syntax) are DROPPED (return <see langword="null" />) rather than risk resolving against the
///     host. Every non-null result is a fully literal, directory-bearing path under the image root - never a bare leaf -
///     so the downstream loader's host env re-expansion and host-System32 leaf fallback are unreachable.
/// </summary>
internal sealed class OfflineImagePathMapper(OfflineImageRoot imageRoot, ITraceLogger? logger)
{
    /// <summary>
    ///     Maps a single registry path value onto the image, or returns <see langword="null" /> when the value cannot be
    ///     safely re-rooted. Callers pass each already-split segment of a multi-value (<c>;</c>-separated) registry string.
    /// </summary>
    public string? Map(string? registryPath)
    {
        if (string.IsNullOrWhiteSpace(registryPath)) { return null; }

        string value = registryPath.Trim().Trim('"').Trim();

        if (value.Length == 0) { return null; }

        // UNC (\\server\share), DOS-device (\\.\), extended-length (\\?\, \\?\UNC\) and NT object (\??\) forms are not
        // collapsed by Path.GetFullPath and could escape the image - reject before any further handling.
        if (value.StartsWith(@"\\", StringComparison.Ordinal) || value.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            return Drop(registryPath, "UNC/DOS-device/NT path form");
        }

        string? relativeToImageRoot = ToImageRootRelative(value);

        if (relativeToImageRoot is null)
        {
            return Drop(registryPath, "unsupported path form");
        }

        // A residual '%' means an unsupported variable (e.g. %ProgramFiles%) we will not guess at; a ':' in the
        // remainder is a stray drive letter or an alternate-data-stream (foo.dll:stream) - both fail closed.
        if (relativeToImageRoot.Contains('%', StringComparison.Ordinal) ||
            relativeToImageRoot.Contains(':', StringComparison.Ordinal))
        {
            return Drop(registryPath, "residual environment token or stream/drive separator");
        }

        string mappedPath = Path.GetFullPath(Path.Combine(imageRoot.ImageRoot, relativeToImageRoot));

        // A '..' segment can normalize above the image root. Drop it here rather than emit an escaping path: the guard
        // would otherwise reject it by THROWING (aborting the whole catalog read for one bad value), whereas an unsafe
        // value should be skipped. This also keeps the "every non-null result is under the image root" invariant true.
        return !imageRoot.ContainsPath(mappedPath) ? Drop(registryPath, "path escapes the image root") : mappedPath;
    }

    private static bool IsDriveAbsolute(string value) =>
        value.Length >= 3 && IsDriveLetter(value[0]) && value[1] == ':' && value[2] is '\\' or '/';

    private static bool IsDriveLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    // Maps a (non-UNC) registry path to the path relative to the image root that it should resolve to, or null when the
    // form is unsafe. Env tokens are mapped to their image-relative location WITHOUT host expansion; a bare leaf is
    // redirected under the image System32 so the result always carries directory information.
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

        if (IsDriveAbsolute(value))
        {
            return value[3..];
        }

        // Drive-relative (e.g. C:foo.dll) resolves against the host's current directory on that drive - reject.
        if (value.Length >= 2 && IsDriveLetter(value[0]) && value[1] == ':')
        {
            return null;
        }

        return value[0] is '\\' or '/' ? value.TrimStart('\\', '/') :
            // Bare leaf or relative subpath: anchor under the image System32 (the leaf-fallback redirect). The combined
            // remainder still carries directory information, so the mapped output is never a bare leaf.
            Path.Combine("Windows", "System32", value);
    }

    // Strips a leading environment token (case-insensitive) plus any immediately following separators, leaving the
    // remainder relative. Returns false (and remainder = "") when value does not start with the token.
    private static bool TryStripTokenPrefix(string value, string token, out string remainder)
    {
        if (value.StartsWith(token, StringComparison.OrdinalIgnoreCase))
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
