// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Scenarios;

/// <summary>
///     Discriminated result for <see cref="IEvtxFolderEnumerator.EnumerateTopLevel" /> so a folder that cannot be read
///     is reported distinctly from one that merely holds no <c>.evtx</c> files.
/// </summary>
public abstract record EvtxFolderScanResult
{
    private EvtxFolderScanResult() { }

    public sealed record Files(ImmutableArray<string> Paths) : EvtxFolderScanResult;

    public sealed record Empty : EvtxFolderScanResult
    {
        private Empty() { }

        public static Empty Instance { get; } = new();
    }

    public sealed record AccessDenied(string Message) : EvtxFolderScanResult;

    public sealed record IoError(string Message) : EvtxFolderScanResult;
}
