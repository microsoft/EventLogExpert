// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace EventLogExpert.DatabaseTools.Common.Ipc;

/// <summary>
///     Source-generated <see cref="JsonSerializerContext" /> for message + request types that flow over the
///     elevation-helper IPC channel. Source-gen mode is <see cref="JsonSourceGenerationMode.Metadata" /> (NOT
///     <c>Serialization</c>) so the runtime can still apply <see cref="RegexJsonConverter" /> via the per-context
///     <see cref="JsonSerializerOptions.Converters" /> collection - the fast-path serialization mode skips runtime
///     converter lookup. Every concrete derived message/request type is listed explicitly so polymorphic deserialization
///     through the base type can locate its metadata.
/// </summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DatabaseToolsIpcMessage))]
[JsonSerializable(typeof(HelloMessage))]
[JsonSerializable(typeof(ProbeMessage))]
[JsonSerializable(typeof(LogMessage))]
[JsonSerializable(typeof(ProgressMessage))]
[JsonSerializable(typeof(ResultMessage))]
[JsonSerializable(typeof(FatalMessage))]
[JsonSerializable(typeof(CancelMessage))]
[JsonSerializable(typeof(DatabaseToolsIpcRequest))]
[JsonSerializable(typeof(ShowProvidersIpcRequest))]
[JsonSerializable(typeof(CreateDatabaseIpcRequest))]
[JsonSerializable(typeof(MergeDatabaseIpcRequest))]
[JsonSerializable(typeof(DiffDatabaseIpcRequest))]
[JsonSerializable(typeof(UpgradeDatabaseIpcRequest))]
internal partial class DatabaseToolsIpcJsonContext : JsonSerializerContext;
