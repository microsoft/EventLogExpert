// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Diagnostics;

namespace EventLogExpert.Eventing.Common.Events;

/// <summary>
///     The frozen, human-readable strings that <see cref="EventResolutionStatus" /> is stored and filtered as;
///     changing one breaks every saved filter that references it.
/// </summary>
public static class ResolutionStatusTokens
{
    public const string Failed = "Resolution error";
    public const string NoMessage = "No message match";
    public const string NoProvider = "No provider metadata";
    public const string Resolved = "Resolved";

    public static EventResolutionStatus Classify(string token)
    {
        switch (token)
        {
            case NoProvider: return EventResolutionStatus.NoProvider;
            case NoMessage: return EventResolutionStatus.NoMessage;
            case Failed: return EventResolutionStatus.Failed;
            case Resolved:
            case "":
                return EventResolutionStatus.Resolved;
            default:
                Debug.Assert(false, $"Unrecognized {nameof(EventResolutionStatus)} token: '{token}'.");
                return EventResolutionStatus.Resolved;
        }
    }

    public static string Format(EventResolutionStatus status) => status switch
    {
        EventResolutionStatus.NoProvider => NoProvider,
        EventResolutionStatus.NoMessage => NoMessage,
        EventResolutionStatus.Failed => Failed,
        _ => Resolved
    };
}
