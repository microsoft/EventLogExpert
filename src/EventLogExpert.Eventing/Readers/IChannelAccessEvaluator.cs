// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Readers;

internal interface IChannelAccessEvaluator
{
    ChannelAccess EvaluateAccess(string? sddl, bool isSecurityChannel);
}
