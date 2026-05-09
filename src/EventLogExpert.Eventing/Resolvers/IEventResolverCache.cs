// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Resolvers;

public interface IEventResolverCache
{
    void ClearAll();

    string GetOrAddDescription(string description);

    string GetOrAddValue(string value);
}
