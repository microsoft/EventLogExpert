// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.ObjectModel;

namespace EventLogExpert.Eventing.PublisherMetadata;

/// <summary>
///     A single provider event in its raw, source-agnostic form: the unresolved fields read from a provider source
///     (the native publisher metadata today, the offline WEVT parser later). <see cref="MessageId" /> and the keyword mask
///     are left unresolved so the shared <see cref="ProviderDetailsAssembler" /> performs the same resolution for every
///     source.
/// </summary>
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

/// <summary>
///     A raw keyword / opcode / task entry: the numeric value plus the two name sources (an inline name and a message
///     id) the assembler resolves into the display name. <see cref="Value" /> carries the native value widened to 64 bits;
///     the assembler applies the per-table key projection.
/// </summary>
internal sealed record RawNamedValue(ulong Value, uint MessageId, string? InlineName);

/// <summary>
///     The source-agnostic raw content of a provider, produced by a provider source and consumed by
///     <see cref="ProviderDetailsAssembler" /> to build a
///     <see cref="EventLogExpert.Provider.Resolution.ProviderDetails" />. The native path (
///     <see cref="ProviderMetadata.ToRawContent" />) produces this today; the offline WEVT parser produces the same shape
///     later, so both feed one assembler.
/// </summary>
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
