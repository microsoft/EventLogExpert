// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterLenses;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLibrary;

// Identifies who initiated a filter-set save so the terminal outcome can be routed back to that initiator.
internal enum SaveFilterSetOrigin
{
    Library,
    Lens,
}

internal sealed record SaveFilterSetAction(
    string Name,
    ImmutableList<SavedFilter> Filters,
    SaveFilterSetOrigin Origin = SaveFilterSetOrigin.Library,
    ImmutableList<FilterLensId>? LensesToClearOnSuccess = null);
