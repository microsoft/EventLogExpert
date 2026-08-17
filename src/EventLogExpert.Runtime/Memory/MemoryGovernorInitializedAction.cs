// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Memory;

internal sealed record MemoryGovernorInitializedAction(long BaselineBytes, long BudgetBytes);
