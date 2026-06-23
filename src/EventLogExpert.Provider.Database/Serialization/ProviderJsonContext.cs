// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Resolution;
using System.Text.Json.Serialization;

namespace EventLogExpert.ProviderDatabase.Serialization;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(MessageModel))]
[JsonSerializable(typeof(EventModel))]
[JsonSerializable(typeof(IReadOnlyList<MessageModel>))]
[JsonSerializable(typeof(IReadOnlyList<EventModel>))]
[JsonSerializable(typeof(IDictionary<long, string>))]
[JsonSerializable(typeof(IDictionary<int, string>))]
[JsonSerializable(typeof(List<MessageModel>))]
[JsonSerializable(typeof(List<EventModel>))]
[JsonSerializable(typeof(Dictionary<long, string>))]
[JsonSerializable(typeof(Dictionary<int, string>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, ValueMapDefinition>))]
[JsonSerializable(typeof(Dictionary<string, ValueMapDefinition>))]
[JsonSerializable(typeof(ValueMapDefinition))]
[JsonSerializable(typeof(ValueMapEntry))]
internal partial class ProviderJsonContext : JsonSerializerContext;
