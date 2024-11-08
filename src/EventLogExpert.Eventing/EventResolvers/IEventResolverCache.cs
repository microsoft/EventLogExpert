// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.EventResolvers;

public interface IEventResolverCache
{
    void ClearAll();

    string GetOrAddDescription(string description);

    string GetOrAddValue(string value);
}
