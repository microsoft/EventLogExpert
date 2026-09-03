// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Drafts;

public abstract record FilterDraftBuildFailure
{
    private FilterDraftBuildFailure() { }

    public sealed record CompilerDiagnostic(string Message) : FilterDraftBuildFailure
    {
        public string Message { get; init; } = Message ?? throw new ArgumentNullException(nameof(Message));
    }

    public sealed record EmptyFilter : FilterDraftBuildFailure;

    public sealed record InvalidBasicStructure : FilterDraftBuildFailure;
}
