// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Security.Principal;

namespace EventLogExpert.Eventing.Resolvers;

public interface IEventResolverCache
{
    void ClearAll();

    string GetOrAddDescription(string description);

    IReadOnlyList<string> GetOrAddKeywords(IReadOnlyList<string> keywords);

    SecurityIdentifier? GetOrAddSid(SecurityIdentifier? sid);

    string GetOrAddValue(string value);
}
