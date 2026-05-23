// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Provider.Schema;

public sealed record DatabaseSchemaState(int CurrentVersion)
{
    public bool NeedsUpgrade => CurrentVersion < DatabaseSchemaVersion.Current;
}
