// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.EventResolvers;

public interface IEventResolverCache
{
    void ClearAll();

    string GetDescription(string description);

    string GetValue(string value);
}
