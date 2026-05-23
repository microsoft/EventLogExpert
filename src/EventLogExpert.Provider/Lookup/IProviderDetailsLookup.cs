// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Models;
using EventLogExpert.Provider.Schema;

namespace EventLogExpert.Provider.Lookup;

public interface IProviderDetailsLookup : IDisposable
{
    string Name { get; }

    ProviderDetails? FindProvider(string providerName);

    DatabaseSchemaState IsUpgradeNeeded();
}
