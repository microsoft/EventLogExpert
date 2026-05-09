// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Eventing.Common.Databases;

public interface IActiveDatabasePathsProvider
{
    ImmutableList<string> ActiveDatabases { get; }
}
