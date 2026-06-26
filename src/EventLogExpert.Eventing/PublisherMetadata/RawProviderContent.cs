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
    /// <summary>Channel reference id to log name; the per-event channel byte is looked up here for the event's log name.</summary>
    public IReadOnlyDictionary<uint, string> Channels { get; init; } = ReadOnlyDictionary<uint, string>.Empty;

    public IReadOnlyList<RawProviderEvent> Events { get; init; } = [];

    public IReadOnlyList<RawNamedValue> Keywords { get; init; } = [];

    public IReadOnlyList<RawNamedValue> Opcodes { get; init; } = [];

    public required string ProviderName { get; init; }

    public required Guid PublisherGuid { get; init; }

    /// <summary>Resolves a message id to its text, or null when unresolved (native FormatMessage never returns null).</summary>
    public required Func<uint, string?> ResolveMessage { get; init; }

    public required string ResourceFilePath { get; init; }

    public IReadOnlyList<RawNamedValue> Tasks { get; init; } = [];
}
