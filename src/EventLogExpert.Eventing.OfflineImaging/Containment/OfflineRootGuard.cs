// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Eventing.OfflineImaging.Containment;

internal sealed class OfflineRootGuardViolationException(string message) : Exception(message);

// Same-machine parity cannot prove isolation; resolve reparse points before opening any offline image file.
internal sealed class OfflineRootGuard(OfflineImageRoot imageRoot, ITraceLogger? logger)
{
    private readonly OfflineImageRoot _imageRoot = imageRoot;
    private readonly ITraceLogger? _logger = logger;

    public void Assert(string path, string what)
    {
        string resolved;

        try
        {
            // Keep normalization, reparse resolution, and containment together so hostile paths fail closed as guard violations.
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
