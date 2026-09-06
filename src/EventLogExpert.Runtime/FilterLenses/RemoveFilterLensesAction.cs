// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLenses;

internal sealed record RemoveFilterLensesAction(ImmutableList<FilterLensId> Ids);
