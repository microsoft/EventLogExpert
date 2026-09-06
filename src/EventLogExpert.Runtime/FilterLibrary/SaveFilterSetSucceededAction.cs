// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterLenses;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed record SaveFilterSetSucceededAction(
    string Name,
    SaveFilterSetOrigin Origin,
    ImmutableList<FilterLensId>? LensesToClearOnSuccess = null);
