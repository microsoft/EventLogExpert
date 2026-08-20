// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Common.Events;

public enum EventResolutionStatus
{
    Resolved,
    NoProvider,
    NoMessage,
    Failed
}
