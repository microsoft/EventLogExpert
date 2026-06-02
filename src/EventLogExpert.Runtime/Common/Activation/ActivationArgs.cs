// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Common.Activation;

/// <summary>
///     Normalized result of inspecting a Windows app activation. <see cref="FilePaths" /> contains paths the producer
///     verified at construction time as pointing at existing <c>.evtx</c> files; <see cref="FolderPaths" /> contains paths
///     the producer verified at construction time as pointing at existing directories. Producers SHOULD drop nonexistent
///     or inaccessible paths before construction (best-effort filtering), but consumers MUST still handle paths that
///     became missing, locked, or otherwise inaccessible AFTER construction — the verification is point-in-time, not a
///     live guarantee, and a TOCTOU window exists between producer-check and dispatcher-use.
/// </summary>
public sealed record ActivationArgs(IReadOnlyList<string> FilePaths, IReadOnlyList<string> FolderPaths)
{
    public static ActivationArgs Empty { get; } = new([], []);

    public bool IsEmpty => FilePaths.Count == 0 && FolderPaths.Count == 0;
}
