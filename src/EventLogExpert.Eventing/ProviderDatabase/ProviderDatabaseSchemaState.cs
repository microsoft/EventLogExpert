// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.ProviderDatabase;

public sealed record ProviderDatabaseSchemaState(int CurrentVersion)
{
    public bool NeedsUpgrade => CurrentVersion < ProviderDatabaseSchemaVersion.Current;
}
