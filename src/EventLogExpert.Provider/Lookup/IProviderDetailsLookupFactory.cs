// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Provider.Lookup;

public interface IProviderDetailsLookupFactory
{
    IProviderDetailsLookup Create(string path);
}
