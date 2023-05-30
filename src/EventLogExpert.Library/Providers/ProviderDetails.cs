// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;

namespace EventLogExpert.Library.Providers;

public class ProviderDetails
{
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>Messages from legacy provider</summary>
    public List<MessageModel> Messages { get; set; } = new List<MessageModel>();

    /// <summary>Events and related items from modern provider</summary>
    public List<EventModel> Events { get; set; } = new List<EventModel>();

    public Dictionary<long, string> Keywords { get; set; } = new Dictionary<long, string>();

    public Dictionary<int, string> Opcodes { get; set; } = new Dictionary<int, string>();

    public Dictionary<int, string> Tasks { get; set; } = new Dictionary<int, string>();
}
