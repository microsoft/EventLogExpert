// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Runtime.Histogram;

public static class HistogramGroups
{
    public static IReadOnlyList<HistogramGroup> Severity { get; } =
    [
        new(new HistogramGroupLabel.SeverityBucket(HistogramSeverityBucket.Other),
            "histogram-bar-normal",
            "sev-other",
            [0, (int)SeverityLevel.Information, (int)SeverityLevel.Verbose]),
        new(new HistogramGroupLabel.SeverityBucket(HistogramSeverityBucket.Warnings),
            "histogram-bar-warning",
            "sev-warning",
            [(int)SeverityLevel.Warning]),
        new(new HistogramGroupLabel.SeverityBucket(HistogramSeverityBucket.Errors),
            "histogram-bar-error",
            "sev-error",
            [(int)SeverityLevel.Critical, (int)SeverityLevel.Error])
    ];

    public static int SeveritySlotCount => LevelSeverity.SlotCount;

    public static IReadOnlyList<HistogramGroup> ForCategories(
        IReadOnlyList<string> keys,
        IReadOnlyList<string> labels,
        HistogramGroupLabel.CategoricalOther? otherLabel)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentOutOfRangeException.ThrowIfNotEqual(labels.Count, keys.Count);

        int otherSlot = keys.Count;
        var groups = new List<HistogramGroup>(keys.Count + (otherLabel is null ? 0 : 1));

        if (otherLabel is not null)
        {
            groups.Add(new HistogramGroup(otherLabel, "histogram-cat-other", "cat-other", [otherSlot]));
        }

        for (int index = 0; index < keys.Count; index++)
        {
            groups.Add(new HistogramGroup(new HistogramGroupLabel.DataValue(labels[index]),
                $"histogram-cat-{index}",
                $"cat:{keys[index]}",
                [index]));
        }

        return groups;
    }
}
