// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable;

public readonly record struct DisplayedIndicator(DisplayIndicatorKind Sentence, bool Spinner)
{
    public static DisplayedIndicator Nothing { get; } = new(DisplayIndicatorKind.None, false);

    public static DisplayedIndicator GenericSpinner { get; } = new(DisplayIndicatorKind.None, true);
}
