// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public sealed record ImportResult(int Imported, IReadOnlyList<ImportFailure> Failures);
