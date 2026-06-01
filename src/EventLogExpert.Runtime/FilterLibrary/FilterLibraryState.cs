// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLibrary;

[FeatureState]
public sealed record FilterLibraryState
{
    public ImmutableList<LibraryEntry> Entries { get; init; } = [];

    public bool IsLoaded { get; init; }

    public bool IsLoading { get; init; }

    public bool LoadError { get; init; }
}
