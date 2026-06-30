// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.ObjectModel;

namespace EventLogExpert.Eventing.PublisherMetadata;

internal sealed record RawProviderEvent(
    uint Id,
    byte Version,
    byte ChannelId,
    byte Level,
    byte Opcode,
    short Task,
    ulong KeywordsMask,
    string Template,
    uint MessageId);

internal sealed record RawNamedValue(ulong Value, uint MessageId, string? InlineName);

internal sealed class RawProviderContent
{
    // Channel keys are native channel reference ids used by each event's channel byte.
    public IReadOnlyDictionary<uint, string> Channels { get; init; } = ReadOnlyDictionary<uint, string>.Empty;

    public IReadOnlyList<RawProviderEvent> Events { get; init; } = [];

    public IReadOnlyList<RawNamedValue> Keywords { get; init; } = [];

    public IReadOnlyList<RawNamedValue> Opcodes { get; init; } = [];

    public required string ProviderName { get; init; }

    public required Guid PublisherGuid { get; init; }

    public required Func<uint, string?> ResolveMessage { get; init; }

    public required string ResourceFilePath { get; init; }

    public IReadOnlyList<RawNamedValue> Tasks { get; init; } = [];
}
