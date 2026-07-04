// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Eventing.OfflineImaging.Wim;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace EventLogExpert.DatabaseTools.Common.Ipc;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(HelloMessage), "hello")]
[JsonDerivedType(typeof(ProbeMessage), "probe")]
[JsonDerivedType(typeof(LogMessage), "log")]
[JsonDerivedType(typeof(ProgressMessage), "progress")]
[JsonDerivedType(typeof(ResultMessage), "result")]
[JsonDerivedType(typeof(FatalMessage), "fatal")]
[JsonDerivedType(typeof(CancelMessage), "cancel")]
[JsonDerivedType(typeof(ImageEditionsMessage), "image-editions")]
public abstract record DatabaseToolsIpcMessage;

public sealed record HelloMessage(int HelperProcessId, int ProtocolVersion) : DatabaseToolsIpcMessage
{
    public const int CurrentProtocolVersion = 3;
}

public sealed record ProbeMessage(
    string ProcessPath,
    string IntegrityLevel,
    bool PackageIdentityOk,
    string? PackageIdentityError,
    bool LocalProviderEnumerationOk,
    string? LocalProviderEnumerationError,
    int LocalProviderCount) : DatabaseToolsIpcMessage;

public sealed record LogMessage(
    DateTime TimestampUtc,
    LogLevel Level,
    string Message,
    string Category = "",
    ProcessOrigin ProcessOrigin = ProcessOrigin.ElevatedHelper) : DatabaseToolsIpcMessage;

public sealed record ProgressMessage(int Processed, int? Total, string? CurrentItem) : DatabaseToolsIpcMessage;

public sealed record ResultMessage(DatabaseToolsOutcome Outcome, string? FailureSummary, long DurationMs) : DatabaseToolsIpcMessage;

public sealed record FatalMessage(string ExceptionType, string Message, string StackTrace) : DatabaseToolsIpcMessage;

public sealed record CancelMessage : DatabaseToolsIpcMessage;

public sealed record ImageEditionsMessage(WimImageListStatus Status, IReadOnlyList<WimImageEntry> Images) : DatabaseToolsIpcMessage;
