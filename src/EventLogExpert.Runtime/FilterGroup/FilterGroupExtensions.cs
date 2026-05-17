// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.Runtime.FilterGroup;

internal static class FilterGroupExtensions
{
    extension(Dictionary<string, FilterGroupNode> group)
    {
        public Dictionary<string, FilterGroupNode> AddFilterGroup(
            string[] groupNames,
            SavedFilterGroup data)
        {
            var root = groupNames.Length <= 1 ? string.Empty : groupNames.First();
            groupNames = [.. groupNames.Skip(1)];

            if (group.TryGetValue(root, out var node))
            {
                if (groupNames.Length > 1)
                {
                    node.ChildNodes.AddFilterGroup(groupNames, data);
                }
                else
                {
                    node.Groups.Add(data);
                }
            }
            else
            {
                group.Add(root,
                    groupNames.Length > 1 ?
                        new FilterGroupNode
                        {
                            ChildNodes = new Dictionary<string, FilterGroupNode>()
                                .AddFilterGroup(groupNames, data)
                        } :
                        new FilterGroupNode { Groups = [data] });
            }

            return group;
        }
    }
}
