// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Eventing.EventResolvers;

public interface IDatabaseCollectionProvider
{
    ImmutableList<string> ActiveDatabases { get; }

    void SetActiveDatabases(IEnumerable<string> activeDatabases);
}
