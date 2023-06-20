// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public record CachedFilterModel
{
    public string ComparisonString { get; set; } = string.Empty;

    public bool IsFavorite { get; set; }

    public bool IsEnabled { get; set; }
}
