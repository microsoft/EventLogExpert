// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace EventLogExpert.DatabaseTools.Contracts;

/// <summary>
///     Source-generated <see cref="JsonSerializerContext" /> for envelope + request types that flow over the
///     elevation-helper IPC channel. Source-gen mode is <see cref="JsonSourceGenerationMode.Metadata" /> (NOT
///     <c>Serialization</c>) so the runtime can still apply <see cref="RegexJsonConverter" /> via the per-context
///     <see cref="JsonSerializerOptions.Converters" /> collection — the fast-path serialization mode skips runtime
///     converter lookup. Every concrete derived envelope/request type is listed explicitly so polymorphic deserialization
///     through the base type can locate its metadata.
/// </summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DatabaseToolsIpcEnvelope))]
[JsonSerializable(typeof(HelloEnvelope))]
[JsonSerializable(typeof(ProbeEnvelope))]
[JsonSerializable(typeof(LogEnvelope))]
[JsonSerializable(typeof(ProgressEnvelope))]
[JsonSerializable(typeof(ResultEnvelope))]
[JsonSerializable(typeof(FatalEnvelope))]
[JsonSerializable(typeof(CancelEnvelope))]
[JsonSerializable(typeof(DatabaseToolsIpcRequest))]
[JsonSerializable(typeof(ShowProvidersIpcRequest))]
[JsonSerializable(typeof(CreateDatabaseIpcRequest))]
[JsonSerializable(typeof(MergeDatabaseIpcRequest))]
[JsonSerializable(typeof(DiffDatabaseIpcRequest))]
[JsonSerializable(typeof(UpgradeDatabaseIpcRequest))]
internal partial class DatabaseToolsIpcJsonContext : JsonSerializerContext;
