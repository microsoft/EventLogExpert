// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using System.Collections.ObjectModel;

namespace EventLogExpert.UI.Models;

public sealed record EventLogData(
    string Name,
    PathType Type,
    ReadOnlyCollection<DisplayEventModel> Events)
{
    public EventLogId Id { get; } = EventLogId.Create();

    /// <summary>Gets a distinct list of values for the specified category.</summary>
    public IEnumerable<string> GetCategoryValues(FilterCategory category) =>
        category switch
        {
            FilterCategory.Id => Events.Select(e => e.Id.ToString()).Distinct(),
            FilterCategory.ActivityId => Events.Select(e => e.ActivityId?.ToString() ?? string.Empty).Distinct(),
            FilterCategory.Level => Enum.GetNames<SeverityLevel>(),
            FilterCategory.KeywordsDisplayNames => Events.SelectMany(e => e.KeywordsDisplayNames).Distinct(),
            FilterCategory.Source => Events.Select(e => e.Source).Distinct(),
            FilterCategory.TaskCategory => Events.Select(e => e.TaskCategory).Distinct(),
            _ => [],
        };
}
