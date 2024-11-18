// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;

namespace EventLogExpert.Eventing.EventResolvers;

/// <summary>Resolves event details from an <see cref="EventRecord" />.</summary>
public interface IEventResolver
{
    public DisplayEventModel ResolveEvent(EventRecord eventRecord, string owningLogName);

    public void ResolveProviderDetails(EventRecord eventRecord, string owningLogName);
}
