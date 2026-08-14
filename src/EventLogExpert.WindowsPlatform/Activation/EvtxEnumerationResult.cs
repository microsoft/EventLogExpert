// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.WindowsPlatform.Activation;

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
