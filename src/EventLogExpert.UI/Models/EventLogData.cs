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

    public IEnumerable<string> GetCategoryValues(FilterCategory category)
    {
        switch (category)
        {
            case FilterCategory.Id:
                return Events.Select(e => e.Id.ToString())
                    .Distinct().Order();
            case FilterCategory.ActivityId:
                return Events.Select(e => e.ActivityId?.ToString() ?? string.Empty)
                    .Distinct().Order();
            case FilterCategory.Level:
                return Enum.GetNames<SeverityLevel>();
            case FilterCategory.KeywordsDisplayNames:
                return Events.SelectMany(e => e.KeywordsDisplayNames)
                    .Distinct().Order();
            case FilterCategory.Source:
                return Events.Select(e => e.Source)
                    .Distinct().Order();
            case FilterCategory.TaskCategory:
                return Events.Select(e => e.TaskCategory)
                    .Distinct().Order();
            case FilterCategory.Xml:
            case FilterCategory.Description:
            default:
                return [];
        }
    }
}
