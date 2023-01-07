// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;

namespace EventLogExpert.Library.Providers;

public class ProviderDetails
{
    public string ProviderName { get; set; }

    /// <summary>Messages from legacy provider</summary>
    public List<MessageModel> Messages { get; set; }

    /// <summary>Events and related items from modern provider</summary>
    public List<EventModel> Events { get; set; }

    public Dictionary<long, string> Keywords { get; set; }

    public Dictionary<long, string> Opcodes { get; set; }

    public Dictionary<long, string> Tasks { get; set; }
}
