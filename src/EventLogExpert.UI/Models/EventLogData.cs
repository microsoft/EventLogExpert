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

    public StringCache ValueCache { get; } = new();

    public List<string> GetCategoryValues(FilterCategory category)
    {
        switch (category)
        {
            case FilterCategory.Id:
                return Events.Select(e => e.Id.ToString())
                    .Distinct().Order().ToList();
            case FilterCategory.ActivityId:
                return Events.Select(e => e.ActivityId?.ToString() ?? string.Empty)
                    .Distinct().Order().ToList();
            case FilterCategory.Level:
                List<string> items = [];

                foreach (SeverityLevel item in Enum.GetValues(typeof(SeverityLevel)))
                {
                    items.Add(item.ToString());
                }

                return items;
            case FilterCategory.KeywordsDisplayNames:
                return Events.SelectMany(e => e.KeywordsDisplayNames)
                    .Distinct().Order().ToList();
            case FilterCategory.Source:
                return Events.Select(e => e.Source)
                    .Distinct().Order().ToList();
            case FilterCategory.TaskCategory:
                return Events.Select(e => e.TaskCategory)
                    .Distinct().Order().ToList();
            case FilterCategory.Xml:
            case FilterCategory.Description:
            default:
                return [];
        }
    }
}
