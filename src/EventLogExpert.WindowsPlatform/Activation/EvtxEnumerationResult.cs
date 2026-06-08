// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.WindowsPlatform.Activation;

/// <summary>
///     Discriminated record hierarchy for <see cref="EvtxFolderEnumerator.EnumerateEvtxTopLevel" /> so callers can
///     apply distinct UX per failure mode without losing diagnostic detail.
/// </summary>
public abstract record EvtxEnumerationResult
{
    private EvtxEnumerationResult() { }

    public sealed record Success(IReadOnlyList<string> Files) : EvtxEnumerationResult;

    public sealed record Empty : EvtxEnumerationResult
    {
        private Empty() { }

        public static Empty Instance { get; } = new();
    }

    public sealed record AccessDenied(string Message) : EvtxEnumerationResult;

    public sealed record IoError(string Message) : EvtxEnumerationResult;
}
