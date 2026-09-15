// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed class ImportValidationException(ImportValidationError error) : JsonException
{
    public ImportValidationError Error { get; } = error ?? throw new ArgumentNullException(nameof(error));
}
