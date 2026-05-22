// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Evaluation;

public static class FilterExtensions
{
    extension(Filter filter)
    {
        public bool HasFilteringChangedFrom(Filter original)
        {
            if (!Equals(filter.DateFilter, original.DateFilter)) { return true; }

            var currentSnapshots = filter.Snapshots;
            var originalSnapshots = original.Snapshots;

            // Default-constructed Filter (rare) has an uninitialized ImmutableArray.
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
