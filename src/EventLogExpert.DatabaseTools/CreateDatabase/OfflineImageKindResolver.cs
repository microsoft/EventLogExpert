// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.DatabaseTools.CreateDatabase;

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
