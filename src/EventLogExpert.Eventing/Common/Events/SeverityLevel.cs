// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Common.Events;

/// <summary>Severity levels with values matching ETW standard levels (1–5).</summary>
public enum SeverityLevel
{
    Critical = 1,
    Error = 2,
    Warning = 3,
    Information = 4,
    Verbose = 5
}
