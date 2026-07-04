// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;
using System.Buffers;
using System.Text.RegularExpressions;

namespace EventLogExpert.Eventing.ProviderMetadata.Wevt;

public static partial class OfflineWevtProviderReader
{
    public static ProviderDetails? TryBuildProviderDetails(
        string resourceFilePath,
        IReadOnlyList<string> messageFilePaths,
        string? parameterFilePath,
        Guid publisherGuid,
        string providerName,
        ILegacyMessageFileResolver legacyResolver,
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

            using MessageTableSession session = MessageTableSession.Open(providerName, candidateFiles, logger);

            RawProviderContent content = MapToRawContent(
                data,
                publisherGuid,
                providerName,
                resourceFilePath,
                messageId => session.Resolve(messageId) is { } raw ? WevtMessageFormatter.Format(raw) : null);

            ProviderDetails details = ProviderDetailsFactory.Create(content, data.Templates, logger);

            PopulateLegacyTables(details, messageFilePaths, parameterFilePath, providerName, legacyResolver, logger);

            ResolveParameterReferences(details);

            return details;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    internal static RawProviderContent MapToRawContent(
        WevtProviderData data,
        Guid publisherGuid,
        string providerName,
        string resourceFilePath,
        Func<uint, string?> resolveMessage)
    {
        // Level table stays unmapped to preserve native RawProviderContent parity.
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

    internal static string ResolveParameterReferences(string description, Func<int, string?> resolveParameterText) =>
        ParameterReferenceRegex().Replace(description, match =>
        {
            string token = match.Value;

            if (!token.StartsWith("%%", StringComparison.Ordinal) ||
                !long.TryParse(token.AsSpan(2), out long parameterId))
            {
                return token;
            }

            string? parameterText = resolveParameterText(unchecked((int)parameterId));

            // Keep unresolved %%NNNN literal so render-time formatting can still use provider/system fallback tables.
            if (string.IsNullOrEmpty(parameterText)) { return token; }

            return parameterText.EndsWith("%0", StringComparison.Ordinal) ? parameterText[..^2] : parameterText;
        }).TrimEnd('\0', '\r', '\n', '\t', ' ');

    private static IReadOnlyDictionary<uint, string> BuildChannels(IReadOnlyList<WevtChannelEntry> channels)
    {
        Dictionary<uint, string> result = [];

        foreach (WevtChannelEntry channel in channels)
        {
            // Native channel dictionaries use first inline ChannelReferencePath by reference id, with no message lookup.
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

            // Unrepresentable template bytes fail closed to an empty template instead of emitting guessed XML.
            string template = source.Template is null
                ? string.Empty
                : WevtTemplateWriter.Write(source.Template.Nodes, source.Template.Descriptors) ?? string.Empty;

            result[index] = new RawProviderEvent(
                source.Id,
                source.Version,
                source.Channel,
                source.Level,
                source.Opcode,
                unchecked((short)source.Task),
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

    [GeneratedRegex("%+[0-9]+")]
    private static partial Regex ParameterReferenceRegex();

    private static void PopulateLegacyTables(
        ProviderDetails details,
        IReadOnlyList<string> modernMessageFilePaths,
        string? parameterFilePath,
        string providerName,
        ILegacyMessageFileResolver legacyResolver,
        ITraceLogger? logger)
    {
        IReadOnlyList<string> legacyFiles = legacyResolver.GetMessageFilesForLegacyProvider(providerName);

        LegacyMessageFileSource? messages = LegacyMessageFileSource.TryCreate(legacyFiles, providerName, logger)
            ?? LegacyMessageFileSource.TryCreate(modernMessageFilePaths, providerName, logger);

        if (messages is not null) { details.SetLazyMessageSource(messages); }

        if (string.IsNullOrEmpty(parameterFilePath)) { return; }

        LegacyMessageFileSource? parameters = LegacyMessageFileSource.TryCreate([parameterFilePath], providerName, logger);

        if (parameters is not null) { details.SetLazyParameterSource(parameters); }
    }

    private static void ResolveParameterReferences(ProviderDetails details)
    {
        foreach (EventModel model in details.Events)
        {
            string? description = model.Description;

            if (string.IsNullOrEmpty(description) || !description.Contains("%%", StringComparison.Ordinal))
            {
                continue;
            }

            model.Description = ResolveParameterReferences(description, rawId => details.GetParameterByRawId(rawId)?.Text);
        }
    }
}
