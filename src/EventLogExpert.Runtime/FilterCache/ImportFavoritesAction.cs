// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterCache;

internal sealed record ImportFavoritesAction(ImmutableList<string> Filters);
