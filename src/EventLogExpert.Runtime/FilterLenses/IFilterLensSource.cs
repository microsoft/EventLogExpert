// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Sources;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLenses;

public interface IFilterLensSource : IChangeNotifier
{
    ImmutableList<FilterLensSummary> Lenses { get; }
}
