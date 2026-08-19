// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Common.Filtering;
using EventLogExpert.Runtime.FilterLenses;
using System.Globalization;

namespace EventLogExpert.Runtime.Stats;

/// <summary>
///     The contributor dimensions the statistics panel ranks (severity is handled separately). All four are
///     filterable, so each supports one-click exclude.
/// </summary>
public enum StatsDimension
{
    Source,
    EventId,
    TaskCategory,
    User
}

public static class StatsDimensions
{
    /// <summary>The contributor dimensions in default display order.</summary>
    public static readonly IReadOnlyList<StatsDimension> All =
        [StatsDimension.Source, StatsDimension.EventId, StatsDimension.TaskCategory, StatsDimension.User];

    extension(StatsDimension dimension)
    {
        public string DisplayName() => dimension switch
        {
            StatsDimension.Source => "Source",
            StatsDimension.EventId => "Event ID",
            StatsDimension.TaskCategory => "Task Category",
            StatsDimension.User => "User",
            _ => dimension.ToString()
        };

        /// <summary>The filter property a contributor row excludes on (verified filterable for all four dimensions).</summary>
        public EventProperty FilterProperty() => dimension switch
        {
            StatsDimension.Source => EventProperty.Source,
            StatsDimension.EventId => EventProperty.Id,
            StatsDimension.TaskCategory => EventProperty.TaskCategory,
            StatsDimension.User => EventProperty.UserDisplayName,
            _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "Unknown stats dimension.")
        };

        /// <summary>Pushes an include ("filter to only") or exclude ("filter out") lens for a contributor value of this dimension.</summary>
        public void PushRowFilter(IFilterLensCommands commands, string value, string? originLog, bool include)
        {
            ArgumentNullException.ThrowIfNull(commands);

            if (dimension == StatsDimension.EventId)
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int eventId))
                {
                    if (include) { commands.IncludeEventId(eventId, originLog); }
                    else { commands.ExcludeEventId(eventId, originLog); }
                }

                return;
            }

            EventProperty property = dimension.FilterProperty();

            if (include) { commands.IncludeValue(property, value, originLog); }
            else { commands.ExcludeValue(property, value, originLog); }
        }

        /// <summary>
        ///     The pooled columnar field a string dimension counts over. Event ID counts through <c>CountEventIds</c>, not a
        ///     field.
        /// </summary>
        internal EventFieldId FieldId() => dimension switch
        {
            StatsDimension.Source => EventFieldId.Source,
            StatsDimension.TaskCategory => EventFieldId.TaskCategory,
            StatsDimension.User => EventFieldId.UserDisplayName,
            _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "Dimension has no pooled string field.")
        };
    }
}
