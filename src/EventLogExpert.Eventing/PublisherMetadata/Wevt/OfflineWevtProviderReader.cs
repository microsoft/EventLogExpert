// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;
using System.Buffers;

namespace EventLogExpert.Eventing.PublisherMetadata.Wevt;

/// <summary>
///     Builds a <see cref="ProviderDetails" /> purely from a provider's WEVT_TEMPLATE and RT_MESSAGETABLE resources,
///     with no EvtOpenPublisherMetadata call. The parsed tables are mapped to <see cref="RawProviderContent" /> in the
///     same representation the native path produces, so the shared <see cref="ProviderDetailsAssembler" /> resolves both
///     sources identically.
/// </summary>
internal static class OfflineWevtProviderReader
{
    /// <summary>
    ///     Maps the parsed provider tables to <see cref="RawProviderContent" />. Pure and host-independent: the unit
    ///     tests drive it directly from crafted bytes plus a fake <paramref name="resolveMessage" />.
    /// </summary>
    internal static RawProviderContent MapToRawContent(
        WevtProviderData data,
        Guid publisherGuid,
        string providerName,
        string resourceFilePath,
        Func<uint, string?> resolveMessage)
    {
        // Levels are parsed for fidelity but intentionally not mapped: the native RawProviderContent carries no level
        // table (event Level is the raw byte on each event), so omitting them keeps parity with the native path.
        return new RawProviderContent
        {
            ProviderName = providerName,
            PublisherGuid = publisherGuid,
            ResourceFilePath = resourceFilePath,
            ResolveMessage = resolveMessage,
            Channels = BuildChannels(data.Channels),
            Events = BuildEvents(data.Events),
            Keywords = BuildKeywords(data.Keywords),
            Opcodes = BuildOpcodes(data.Opcodes),
            Tasks = BuildTasks(data.Tasks)
        };
    }

    internal static ProviderDetails? TryBuildProviderDetails(
        string resourceFilePath,
        IReadOnlyList<string> messageFilePaths,
        Guid publisherGuid,
        string providerName,
        ITraceLogger? logger)
    {
        byte[]? rented = WevtTemplateReader.TryRentWevtResource(resourceFilePath, logger, out int resourceSize);

        if (rented is null)
        {
            return null;
        }

        try
        {
            WevtProviderData? data = WevtTemplateReader.TryParseProvider(
                rented.AsSpan(0, resourceSize),
                publisherGuid,
                logger);

            if (data is null)
            {
                logger?.Debug($"{nameof(OfflineWevtProviderReader)}: provider {publisherGuid} not found in {resourceFilePath}.");

                return null;
            }

            List<string> candidateFiles = [.. messageFilePaths, resourceFilePath];

            // The session holds native message-table handles and backs RawProviderContent.ResolveMessage; the closure is
            // only invoked synchronously during Assemble, so the session is disposed immediately after it returns.
            using MessageTableSession session = MessageTableSession.Open(providerName, candidateFiles, logger);

            RawProviderContent content = MapToRawContent(
                data,
                publisherGuid,
                providerName,
                resourceFilePath,
                session.Resolve);

            return ProviderDetailsAssembler.Assemble(content, data.Templates, logger);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static IReadOnlyDictionary<uint, string> BuildChannels(IReadOnlyList<WevtChannelEntry> channels)
    {
        Dictionary<uint, string> result = [];

        foreach (WevtChannelEntry channel in channels)
        {
            // Inline-only, keyed by the channel reference id, first row wins - matching the native channel dictionary,
            // which uses the inline ChannelReferencePath with no message resolution.
            if (channel.InlineName is { Length: > 0 } name)
            {
                result.TryAdd(channel.ReferenceId, name);
            }
        }

        return result;
    }

    private static IReadOnlyList<RawProviderEvent> BuildEvents(IReadOnlyList<WevtProviderEvent> events)
    {
        RawProviderEvent[] result = new RawProviderEvent[events.Count];

        for (int index = 0; index < events.Count; index++)
        {
            WevtProviderEvent source = events[index];

            // Struct-bearing templates fail closed (no flat synthesis), as do templates the synthesizer rejects as
            // unrepresentable (unknown inType/outType byte or a non-field-reference length); both yield an empty string.
            string template = source.Template is null || source.Template.IsStruct
                ? string.Empty
                : WevtTemplateSynthesizer.Synthesize(source.Template.Items) ?? string.Empty;

            result[index] = new RawProviderEvent(
                source.Id,
                source.Version,
                source.Channel,
                source.Level,
                source.Opcode,
                (short)source.Task,
                source.Keywords,
                template,
                source.MessageId);
        }

        return result;
    }

    private static IReadOnlyList<RawNamedValue> BuildKeywords(IReadOnlyList<WevtKeywordEntry> keywords)
    {
        RawNamedValue[] result = new RawNamedValue[keywords.Count];

        for (int index = 0; index < keywords.Count; index++)
        {
            WevtKeywordEntry keyword = keywords[index];

            result[index] = new RawNamedValue(keyword.Mask, keyword.MessageId, keyword.InlineName);
        }

        return result;
    }

    private static IReadOnlyList<RawNamedValue> BuildOpcodes(IReadOnlyList<WevtIdentifiedEntry> opcodes)
    {
        RawNamedValue[] result = new RawNamedValue[opcodes.Count];

        for (int index = 0; index < opcodes.Count; index++)
        {
            WevtIdentifiedEntry opcode = opcodes[index];

            // The OPCO table stores each opcode value already shifted into the high word (opcode << 16) - the exact layout
            // native EvtPublisherMetadataOpcodeValue returns and the assembler projects via (int)((uint)Value >> 16). Pass
            // the raw id through unchanged so the offline and native opcode keys match.
            result[index] = new RawNamedValue(opcode.Id, opcode.MessageId, opcode.InlineName);
        }

        return result;
    }

    private static IReadOnlyList<RawNamedValue> BuildTasks(IReadOnlyList<WevtIdentifiedEntry> tasks)
    {
        RawNamedValue[] result = new RawNamedValue[tasks.Count];

        for (int index = 0; index < tasks.Count; index++)
        {
            WevtIdentifiedEntry task = tasks[index];

            result[index] = new RawNamedValue(task.Id, task.MessageId, task.InlineName);
        }

        return result;
    }
}
