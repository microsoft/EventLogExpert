// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.DetailsPane;

public static class DetailsPropertyText
{
    public static string Invariant(DetailsPropertyLabel label) => label switch
    {
        DetailsPropertyLabel.Source => "Source",
        DetailsPropertyLabel.DateTime => "Date and Time",
        DetailsPropertyLabel.Computer => "Computer",
        DetailsPropertyLabel.LogName => "Log Name",
        DetailsPropertyLabel.TaskCategory => "Task Category",
        DetailsPropertyLabel.Opcode => "Opcode",
        DetailsPropertyLabel.ResolutionStatus => "Resolution Status",
        DetailsPropertyLabel.Keywords => "Keywords",
        DetailsPropertyLabel.RecordId => "Record ID",
        DetailsPropertyLabel.ProcessId => "Process ID",
        DetailsPropertyLabel.ThreadId => "Thread ID",
        DetailsPropertyLabel.ActivityId => "Activity ID",
        DetailsPropertyLabel.RelatedActivityId => "Related Activity ID",
        DetailsPropertyLabel.User => "User",
        DetailsPropertyLabel.UserSid => "User SID",
        _ => throw new ArgumentOutOfRangeException(nameof(label), label, null)
    };
}
