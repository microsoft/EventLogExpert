// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Filter;

internal static class EventFilterExtensions
{
    extension(EventFilter eventFilter)
    {
        public bool HasFilteringChangedFrom(EventFilter original)
        {
            if (!Equals(eventFilter.DateFilter, original.DateFilter)) { return true; }

            var currentSnapshots = eventFilter.Snapshots;
            var originalSnapshots = original.Snapshots;

            // Default-constructed EventFilter (rare) has an uninitialized ImmutableArray.
            if (currentSnapshots.IsDefault || originalSnapshots.IsDefault)
            {
                return currentSnapshots.IsDefault != originalSnapshots.IsDefault;
            }

            if (currentSnapshots.Length != originalSnapshots.Length) { return true; }

            for (int index = 0; index < currentSnapshots.Length; index++)
            {
                if (!currentSnapshots[index].Equals(originalSnapshots[index])) { return true; }
            }

            return false;
        }
    }
}
