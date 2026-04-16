// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;

namespace EventLogExpert.Eventing.EventResolvers;

/// <summary>Resolves event details from an <see cref="EventRecord" />.</summary>
public interface IEventResolver : IDisposable
{
    DisplayEventModel ResolveEvent(EventRecord eventRecord);

    void ResolveProviderDetails(EventRecord eventRecord);

    void SetMetadataPaths(IReadOnlyList<string> metadataPaths);
}
