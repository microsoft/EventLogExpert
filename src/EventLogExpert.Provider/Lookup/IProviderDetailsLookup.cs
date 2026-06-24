// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Resolution;
using EventLogExpert.Provider.Schema;

namespace EventLogExpert.Provider.Lookup;

public interface IProviderDetailsLookup : IDisposable
{
    string Name { get; }

    IReadOnlyList<ProviderDetails> FindAllProviderVersions(string providerName);

    ProviderDetails? FindProvider(string providerName);

    DatabaseSchemaState IsUpgradeNeeded();
}
