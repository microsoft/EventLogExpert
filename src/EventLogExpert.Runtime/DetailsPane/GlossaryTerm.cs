// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.DetailsPane;

/// <summary>
///     Identifies a curated glossary description for a structured EventData field. The explainer resolves this typed
///     identity (never a resource key); the UI maps it to a localized description via <c>GlossaryLocalizer</c>. The
///     event-scoped members carry the event id because the same field name means different things per event (a 4624 logon
///     vs a 4625 failure). Display-only text, so it has no invariant map (it is never copied).
/// </summary>
public enum GlossaryTerm
{
    TargetUserName4624,
    SubjectUserName4624,
    TargetUserName4625,
    AuthenticationPackageName,
    LogonProcessName,
    LogonType,
    IpAddress,
    IpPort,
    ProcessName,
    CommandLine,
    WorkstationName
}
