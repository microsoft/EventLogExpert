// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Readers;

namespace EventLogExpert.Eventing.Resolvers;

/// <summary>Resolves event details from an <see cref="EventRecord" />.</summary>
public interface IEventResolver : IDisposable
{
    void LoadProviderDetails(EventRecord eventRecord);

    ResolvedEvent ResolveEvent(EventRecord eventRecord);

    void SetMetadataPaths(IReadOnlyList<string> metadataPaths);
}
