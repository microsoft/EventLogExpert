// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.Histogram;

public sealed record HistogramGroup(string Label, string ColorClass, string Key, int[] SlotIndices);

public static class HistogramGroups
{
    public static IReadOnlyList<HistogramGroup> Severity { get; } =
    [
        new("Other", "histogram-bar-normal", "sev-other", [0, (int)SeverityLevel.Information, (int)SeverityLevel.Verbose]),
        new("Warnings", "histogram-bar-warning", "sev-warning", [(int)SeverityLevel.Warning]),
        new("Errors", "histogram-bar-error", "sev-error", [(int)SeverityLevel.Critical, (int)SeverityLevel.Error])
    ];

    public static int SeveritySlotCount => LevelSeverity.SlotCount;

    public static IReadOnlyList<HistogramGroup> ForCategories(IReadOnlyList<string> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        int otherSlot = labels.Count;
        var groups = new List<HistogramGroup>(labels.Count + 1)
        {
            new("Other", "histogram-cat-other", "cat-other", [otherSlot])
        };

        for (int index = 0; index < labels.Count; index++)
        {
            groups.Add(new HistogramGroup(labels[index], $"histogram-cat-{index}", $"cat:{labels[index]}", [index]));
        }

        return groups;
    }
}
