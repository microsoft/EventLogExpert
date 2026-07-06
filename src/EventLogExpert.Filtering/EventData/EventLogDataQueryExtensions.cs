// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Filtering.Common.Filtering;
using System.Globalization;

namespace EventLogExpert.Filtering.EventData;

internal static class EventLogDataQueryExtensions
{
    extension(IEnumerable<ResolvedEvent> events)
    {
        /// <summary>
        ///     Gets the distinct set of &lt;EventData&gt; field names present across <paramref name="events" /> (used to
        ///     populate the Basic editor's field-name picker). Names are compared Ordinal, matching the schema lookup.
        /// </summary>
        public IEnumerable<string> GetEventDataFieldNames()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var resolvedEvent in events)
            {
                foreach (var field in resolvedEvent.EventData)
                {
                    if (seen.Add(field.Name)) { yield return field.Name; }
                }
            }
        }

        /// <summary>
        ///     Gets the values of the named EventData <paramref name="fieldName" /> across every event that has it (as
        ///     rendered by <see cref="EventFieldValue.AsString" />); the caller de-duplicates and sorts.
        /// </summary>
        public IEnumerable<string> GetEventDataFieldValues(string fieldName)
        {
            foreach (var resolvedEvent in events)
            {
                if (resolvedEvent.EventData.TryGetValue(fieldName, out var value))
                {
                    yield return value.AsString();
                }
            }
        }

        /// <summary>
        ///     Gets a distinct list of values for the specified <paramref name="property" /> across
        ///     <paramref name="events" />.
        /// </summary>
        public IEnumerable<string> GetEventValues(EventProperty property) =>
            property switch
            {
                EventProperty.Id => events.Select(e => e.Id.ToString(CultureInfo.InvariantCulture)).Distinct(),
                EventProperty.ActivityId => events.Select(e => e.ActivityId?.ToString() ?? string.Empty).Distinct(),
                EventProperty.Level => Enum.GetNames<SeverityLevel>(),
                EventProperty.Keywords => events.SelectMany(e => e.Keywords).Distinct(),
                EventProperty.Source => events.Select(e => e.Source).Distinct(),
                EventProperty.TaskCategory => events.Select(e => e.TaskCategory).Distinct(),
                EventProperty.LogName => events.Select(e => e.LogName).Distinct(),
                _ => [],
            };

        /// <summary>
        ///     Distinct structured &lt;UserData&gt; field paths (storage keys) across <paramref name="events" />, for the
        ///     Basic editor's UserData field-name picker. Compared Ordinal, matching the storage-key lookup.
        /// </summary>
        public IEnumerable<string> GetUserDataFieldNames()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var resolvedEvent in events)
            {
                if (resolvedEvent.UserData.IsDefaultOrEmpty) { continue; }

                foreach (var field in resolvedEvent.UserData)
                {
                    if (seen.Add(field.Path)) { yield return field.Path; }
                }
            }
        }

        /// <summary>
        ///     Values of the structured UserData field <paramref name="fieldName" /> (a storage key) across every event that
        ///     has it; a repeating field contributes each collapsed value. The caller de-duplicates and sorts.
        /// </summary>
        public IEnumerable<string> GetUserDataFieldValues(string fieldName)
        {
            foreach (var resolvedEvent in events)
            {
                if (resolvedEvent.UserData.IsDefaultOrEmpty) { continue; }

                foreach (var field in resolvedEvent.UserData)
                {
                    if (!string.Equals(field.Path, fieldName, StringComparison.Ordinal) || field.Values.IsDefaultOrEmpty)
                    {
                        continue;
                    }

                    foreach (var value in field.Values)
                    {
                        yield return value;
                    }
                }
            }
        }
    }
}
