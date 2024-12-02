// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;

namespace EventLogExpert.Eventing.Providers;

public class ProviderDetails
{
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>Messages from legacy provider</summary>
    public IEnumerable<MessageModel> Messages { get; set; } = [];

    /// <summary>Parameter strings from legacy provider</summary>
    public IEnumerable<MessageModel> Parameters { get; set; } = [];

    /// <summary>Events and related items from modern provider</summary>
    public IEnumerable<EventModel> Events { get; set; } = [];

    public IDictionary<long, string> Keywords { get; set; } = new Dictionary<long, string>();

    public IDictionary<int, string> Opcodes { get; set; } = new Dictionary<int, string>();

    public IDictionary<int, string> Tasks { get; set; } = new Dictionary<int, string>();
}
