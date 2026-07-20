// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Readers;

public enum ChannelAccess
{
    Accessible,
    RequiresElevation,
    Unknown,
    NotEvaluated
}
