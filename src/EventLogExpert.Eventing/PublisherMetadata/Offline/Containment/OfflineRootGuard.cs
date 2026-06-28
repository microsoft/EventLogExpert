// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.PublisherMetadata.Offline.Containment;

/// <summary>Thrown when an offline image build is about to open a file path that escapes the image root.</summary>
internal sealed class OfflineRootGuardViolationException(string message) : Exception(message);

/// <summary>
///     Fail-closed guard asserting that every file the offline pipeline opens lies under the image root. Same-machine
///     parity testing CANNOT detect host contamination (a host DLL resolves to the same content as the image's), so this
///     is the real isolation gate. A purely lexical prefix check is insufficient because the file open transparently
///     follows NTFS junctions / symlinks, so <see cref="Assert" /> resolves reparse points in the path chain first and
///     verifies the FINAL target is still inside the image; anything outside throws.
/// </summary>
internal sealed class OfflineRootGuard(OfflineImageRoot imageRoot, ITraceLogger? logger)
{
    private readonly OfflineImageRoot _imageRoot = imageRoot;
    private readonly ITraceLogger? _logger = logger;

    /// <summary>
    ///     Throws <see cref="OfflineRootGuardViolationException" /> when <paramref name="path" /> resolves outside the
    ///     image root. Reparse points in the existing portion of the path are resolved to their final target first so a
    ///     junction whose target leaves the image is caught here rather than silently followed by the file open; if that
    ///     resolution itself fails, the path is rejected (fail closed) rather than accepted on its unresolved lexical form.
    /// </summary>
    public void Assert(string path, string what)
    {
        string resolved;

        try
        {
            // GetFullPath, the reparse walk, and the (GetFullPath-based) containment check all go inside the try so a
            // hostile junction whose final target is malformed / extended-form / too long fails closed as a violation
            // rather than throwing a raw path exception past the resolver's drop translation. Filter mirrors the mapper.
            resolved = OfflineImageRoot.ResolveReparsePoints(Path.GetFullPath(path));

            if (_imageRoot.ContainsPath(resolved)) { return; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PathTooLongException or NotSupportedException)
        {
            throw Violation(path, what, $"its reparse points could not be resolved ({ex.Message})");
        }

        throw Violation(path, what, $"resolves to '{resolved}'");
    }

    private OfflineRootGuardViolationException Violation(string path, string what, string detail)
    {
        string message = $"Offline {what} path '{path}' {detail}, which is outside the image root.";
        _logger?.Warning($"{nameof(OfflineRootGuard)}: {message}");

        return new OfflineRootGuardViolationException(message);
    }
}
