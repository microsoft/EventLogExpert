// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Provider.Resolution;

public interface IActiveDatabases
{
    ImmutableList<string> Paths { get; }
}
