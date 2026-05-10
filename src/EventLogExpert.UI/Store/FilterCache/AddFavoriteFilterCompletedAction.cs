// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterCache;

public sealed record AddFavoriteFilterCompletedAction(ImmutableList<string> Filters);
