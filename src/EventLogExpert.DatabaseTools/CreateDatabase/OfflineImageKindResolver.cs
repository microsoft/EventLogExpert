// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.DatabaseTools.CreateDatabase;

/// <summary>
///     Resolves how an offline-image path is read: an explicit kind wins, otherwise it is inferred from the path - an
///     existing directory is a mounted volume / extracted folder, a <c>.wim</c>/<c>.esd</c> is a WIM, a <c>.iso</c> is an
///     ISO, and a <c>.vhdx</c>/<c>.vhd</c> is a virtual disk. Path-based and public so both the in-process create
///     operation and the elevation helper (a separate assembly with no <c>InternalsVisibleTo</c>) share one source of
///     truth for the extension-to-kind mapping rather than each re-deriving it.
/// </summary>
public static class OfflineImageKindResolver
{
    public static OfflineImageKind? ResolveFromPath(string? imagePath, OfflineImageKind? explicitKind = null)
    {
        if (explicitKind is { } kind) { return kind; }

        if (string.IsNullOrWhiteSpace(imagePath)) { return null; }

        if (Directory.Exists(imagePath)) { return OfflineImageKind.Directory; }

        string extension = Path.GetExtension(imagePath.TrimEnd('.', ' '));

        if (extension.Equals(".wim", StringComparison.OrdinalIgnoreCase) || extension.Equals(".esd", StringComparison.OrdinalIgnoreCase))
        {
            return OfflineImageKind.Wim;
        }

        if (extension.Equals(".iso", StringComparison.OrdinalIgnoreCase)) { return OfflineImageKind.Iso; }

        return extension.Equals(".vhdx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".vhd", StringComparison.OrdinalIgnoreCase)
            ? OfflineImageKind.Vhdx
            : null;
    }
}
