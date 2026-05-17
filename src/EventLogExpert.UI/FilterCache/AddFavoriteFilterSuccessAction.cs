// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.UI.FilterCache;

internal sealed record AddFavoriteFilterSuccessAction(ImmutableList<string> Filters);
