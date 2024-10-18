// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using System.Diagnostics.Eventing.Reader;

namespace EventLogExpert.Eventing.EventResolvers;

/// <summary>
///     Turns a System.Diagnostics.Eventing.Reader.EventRecord into an
///     EventLogExpert.Eventing.Models.DisplayEventModel.
/// </summary>
public interface IEventResolver : IDisposable
{
    public static readonly StringCache ValueCache = new();

    public IEnumerable<string> GetKeywordsFromBitmask(EventRecord eventRecord);

    public string ResolveDescription(EventRecord eventRecord);

    public void ResolveProviderDetails(EventRecord eventRecord, string owningLogName);

    public string ResolveTaskName(EventRecord eventRecord);
}
