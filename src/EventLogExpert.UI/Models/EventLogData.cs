// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using System.Collections.ObjectModel;

namespace EventLogExpert.UI.Models;

public readonly record struct EventLogData(
    string Name,
    LogType Type,
    ReadOnlyCollection<DisplayEventModel> Events)
{
    public EventLogId Id { get; } = EventLogId.Create();

    /// <summary>Gets a distinct list of values for the specified category.</summary>
    public IEnumerable<string> GetCategoryValues(FilterCategory category)
    {
        switch (category)
        {
            case FilterCategory.Id:
                return Events.Select(e => e.Id.ToString()).Distinct();
            case FilterCategory.ActivityId:
                return Events.Select(e => e.ActivityId?.ToString() ?? string.Empty).Distinct();
            case FilterCategory.Level:
                return Enum.GetNames<SeverityLevel>();
            case FilterCategory.KeywordsDisplayNames:
                return Events.SelectMany(e => e.KeywordsDisplayNames).Distinct();
            case FilterCategory.Source:
                return Events.Select(e => e.Source).Distinct();
            case FilterCategory.TaskCategory:
                return Events.Select(e => e.TaskCategory).Distinct();
            case FilterCategory.Xml:
            case FilterCategory.Description:
            default:
                return [];
        }
    }
}
