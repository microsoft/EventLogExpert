// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.UI.FilterEditor;

public sealed record CachedFilterOption(string Value, bool IsFavorite, ImmutableList<string> Tags);
