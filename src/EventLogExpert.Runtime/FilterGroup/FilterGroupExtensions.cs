// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterGroup;

internal static class FilterGroupExtensions
{
    extension(ImmutableDictionary<string, FilterGroupNode> group)
    {
        public ImmutableDictionary<string, FilterGroupNode> AddFilterGroup(
            string[] groupNames,
            SavedFilterGroup data)
        {
            ArgumentNullException.ThrowIfNull(groupNames);

            var root = groupNames.Length <= 1 ? string.Empty : groupNames.First();
            var remaining = groupNames.Length <= 1 ? [] : groupNames.Skip(1).ToArray();

            if (group.TryGetValue(root, out var node))
            {
                var updated = remaining.Length > 1
                    ? node with { ChildNodes = node.ChildNodes.AddFilterGroup(remaining, data) }
                    : node with { Groups = node.Groups.Add(data) };

                return group.SetItem(root, updated);
            }

            var newNode = remaining.Length > 1
                ? new FilterGroupNode
                {
                    ChildNodes = ImmutableDictionary<string, FilterGroupNode>.Empty.AddFilterGroup(remaining, data)
                }
                : new FilterGroupNode { Groups = ImmutableList.Create(data) };

            return group.Add(root, newNode);
        }
    }
}
