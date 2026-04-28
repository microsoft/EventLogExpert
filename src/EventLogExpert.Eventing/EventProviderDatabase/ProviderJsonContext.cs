// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using System.Text.Json.Serialization;

namespace EventLogExpert.Eventing.EventProviderDatabase;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(MessageModel))]
[JsonSerializable(typeof(EventModel))]
[JsonSerializable(typeof(IReadOnlyList<MessageModel>))]
[JsonSerializable(typeof(IEnumerable<MessageModel>))]
[JsonSerializable(typeof(IReadOnlyList<EventModel>))]
[JsonSerializable(typeof(IDictionary<long, string>))]
[JsonSerializable(typeof(IDictionary<int, string>))]
[JsonSerializable(typeof(List<MessageModel>))]
[JsonSerializable(typeof(List<EventModel>))]
[JsonSerializable(typeof(Dictionary<long, string>))]
[JsonSerializable(typeof(Dictionary<int, string>))]
internal partial class ProviderJsonContext : JsonSerializerContext;
