// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;

namespace EventLogExpert.Eventing.EventResolvers;

/// <summary>Resolves event details from an <see cref="EventRecord" />.</summary>
public interface IEventResolver
{
    public IEnumerable<string> GetKeywordsFromBitmask(EventRecord eventRecord);

    public string GetXml(EventRecord eventRecord);

    public string ResolveDescription(EventRecord eventRecord);

    public void ResolveProviderDetails(EventRecord eventRecord, string owningLogName);

    public string ResolveTaskName(EventRecord eventRecord);
}
