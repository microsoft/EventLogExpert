// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Providers;

namespace EventLogExpert.Eventing.ProviderDatabase;

public interface IProviderDetailsLookup : IDisposable
{
    string Name { get; }

    ProviderDetails? FindProvider(string providerName);

    ProviderDatabaseSchemaState IsUpgradeNeeded();
}
