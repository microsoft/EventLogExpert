// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;

namespace EventLogExpert.Eventing.Providers;

public class ProviderDetails
{
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>Messages from legacy provider</summary>
    public List<MessageModel> Messages { get; set; } = [];

    /// <summary>Parameter strings from legacy provider</summary>
    public List<MessageModel> Parameters { get; set; } = [];

    /// <summary>Events and related items from modern provider</summary>
    public List<EventModel> Events { get; set; } = [];

    public Dictionary<long, string> Keywords { get; set; } = [];

    public Dictionary<int, string> Opcodes { get; set; } = [];

    public Dictionary<int, string> Tasks { get; set; } = [];
}
