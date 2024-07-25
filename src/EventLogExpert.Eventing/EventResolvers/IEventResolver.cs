// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Diagnostics.Eventing.Reader;

namespace EventLogExpert.Eventing.EventResolvers;

/// <summary>
///     Turns a System.Diagnostics.Eventing.Reader.EventRecord into an
///     EventLogExpert.Eventing.Models.DisplayEventModel.
/// </summary>
public interface IEventResolver : IDisposable
{
    public void ResolveProviderDetails(EventRecord eventRecord, string owningLogName);
}
