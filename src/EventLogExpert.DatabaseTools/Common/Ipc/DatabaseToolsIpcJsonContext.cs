// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.Eventing.OfflineImaging.Wim;
using System.Text.Json.Serialization;

namespace EventLogExpert.DatabaseTools.Common.Ipc;

// Metadata mode is required so RegexJsonConverter is discovered at runtime; fast-path serialization skips converter lookup.
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(DatabaseToolsIpcMessage))]
[JsonSerializable(typeof(HelloMessage))]
[JsonSerializable(typeof(ProbeMessage))]
[JsonSerializable(typeof(LogMessage))]
[JsonSerializable(typeof(ProgressMessage))]
[JsonSerializable(typeof(ResultMessage))]
[JsonSerializable(typeof(FatalMessage))]
[JsonSerializable(typeof(CancelMessage))]
[JsonSerializable(typeof(ImageEditionsMessage))]
[JsonSerializable(typeof(DatabaseToolsIpcRequest))]
[JsonSerializable(typeof(ShowProvidersIpcRequest))]
[JsonSerializable(typeof(CreateDatabaseIpcRequest))]
[JsonSerializable(typeof(MergeDatabaseIpcRequest))]
[JsonSerializable(typeof(DiffDatabaseIpcRequest))]
[JsonSerializable(typeof(UpgradeDatabaseIpcRequest))]
[JsonSerializable(typeof(ListImageEditionsIpcRequest))]
[JsonSerializable(typeof(ListOfflineImageEditionsRequest))]
[JsonSerializable(typeof(WimImageEntry))]
[JsonSerializable(typeof(WimImageList))]
internal partial class DatabaseToolsIpcJsonContext : JsonSerializerContext;
