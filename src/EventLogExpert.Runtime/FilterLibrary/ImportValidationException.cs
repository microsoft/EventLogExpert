// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed class ImportValidationException(ImportValidationError error) : Exception
{
    public ImportValidationError Error { get; } = error ?? throw new ArgumentNullException(nameof(error));
}
