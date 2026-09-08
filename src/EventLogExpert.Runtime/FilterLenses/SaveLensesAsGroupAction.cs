// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.FilterLenses;

internal sealed record SaveLensesAsGroupAction(string Name, bool ClearAfterSave = false);
