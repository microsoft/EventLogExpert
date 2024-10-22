// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.EventResolvers;

/// <summary>
///     Turns a System.Diagnostics.Eventing.Reader.EventRecord into an
///     EventLogExpert.Eventing.Models.DisplayEventModel.
/// </summary>
public interface IEventResolver
{
    //public IEnumerable<string> GetKeywordsFromBitmask(EventRecord eventRecord);

    //public string ResolveDescription(EventRecord eventRecord);

    //public void ResolveProviderDetails(EventRecord eventRecord, string owningLogName);

    //public string ResolveTaskName(EventRecord eventRecord);
}
