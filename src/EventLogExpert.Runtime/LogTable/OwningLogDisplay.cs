// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable;

public static class OwningLogDisplay
{
    // Short names for a set of owning logs, disambiguating any that share a file name (e.g. Security.evtx opened from two folders) with the parent folder, then escalating to the full path if even that repeats, so each log stays a distinct legend label and accessible name.
    public static IReadOnlyList<string> DistinctShortNames(IReadOnlyList<string> owningLogs)
    {
        ArgumentNullException.ThrowIfNull(owningLogs);

        var shortNames = new string[owningLogs.Count];
        var shortNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int index = 0; index < shortNames.Length; index++)
        {
            shortNames[index] = ShortName(owningLogs[index]);
            shortNameCounts[shortNames[index]] = shortNameCounts.GetValueOrDefault(shortNames[index]) + 1;
        }

        var labels = new string[owningLogs.Count];

        for (int index = 0; index < labels.Length; index++)
        {
            string parent = ParentFolder(owningLogs[index]);
            labels[index] = shortNameCounts[shortNames[index]] == 1 || parent.Length == 0
                ? shortNames[index]
                : $"{shortNames[index]} ({parent})";
        }

        EscalateRepeatsToFullPath(labels, owningLogs);

        return labels;
    }

    // The file name for a saved-log path, or the whole value for a live log (no separator).
    public static string ShortName(string owningLog) => owningLog[(owningLog.LastIndexOf('\\') + 1)..];

    private static void EscalateRepeatsToFullPath(string[] labels, IReadOnlyList<string> owningLogs)
    {
        var labelCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (string label in labels) { labelCounts[label] = labelCounts.GetValueOrDefault(label) + 1; }

        for (int index = 0; index < labels.Length; index++)
        {
            if (labelCounts[labels[index]] > 1) { labels[index] = owningLogs[index]; }
        }
    }

    private static string ParentFolder(string owningLog)
    {
        int end = owningLog.LastIndexOf('\\');
        if (end <= 0) { return string.Empty; }

        int start = owningLog.LastIndexOf('\\', end - 1);
        return owningLog[(start + 1)..end];
    }
}
