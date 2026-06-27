// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;
using System.Buffers;
using System.Text.RegularExpressions;

namespace EventLogExpert.Eventing.PublisherMetadata.Wevt;

/// <summary>
///     Builds a <see cref="ProviderDetails" /> purely from a provider's WEVT_TEMPLATE and RT_MESSAGETABLE resources,
///     with no EvtOpenPublisherMetadata call. The parsed tables are mapped to <see cref="RawProviderContent" /> in the
///     same representation the native path produces, so the shared <see cref="ProviderDetailsFactory" /> resolves both
///     sources identically.
/// </summary>
internal static partial class OfflineWevtProviderReader
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

    internal static string ResolveParameterReferences(string description, Func<int, string?> resolveParameterText) =>
        ParameterReferenceRegex().Replace(description, match =>
        {
            string token = match.Value;

            if (!token.StartsWith("%%", StringComparison.Ordinal) ||
                !long.TryParse(token.AsSpan(2), out long parameterId))
            {
                return token;
            }

            string? parameterText = resolveParameterText((int)parameterId);

            // An unresolved reference stays the literal %%NNNN so the render-time DescriptionFormatter can still resolve
            // it (including its system-message-table fallback, which the offline db-create path must not bake in here).
            if (string.IsNullOrEmpty(parameterText)) { return token; }

            return parameterText.EndsWith("%0", StringComparison.Ordinal) ? parameterText[..^2] : parameterText;
        }).TrimEnd('\0', '\r', '\n', '\t', ' ');

    internal static ProviderDetails? TryBuildProviderDetails(
        string resourceFilePath,
        IReadOnlyList<string> messageFilePaths,
        string? parameterFilePath,
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

            using MessageTableSession session = MessageTableSession.Open(providerName, candidateFiles, logger);

            RawProviderContent content = MapToRawContent(
                data,
                publisherGuid,
                providerName,
                resourceFilePath,
                messageId => session.Resolve(messageId) is { } raw ? WevtMessageFormatter.Format(raw) : null);

            ProviderDetails details = ProviderDetailsFactory.Create(content, data.Templates, logger);

            PopulateLegacyTables(details, messageFilePaths, parameterFilePath, providerName, logger);

            ResolveParameterReferences(details);

            return details;
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

            // An unrepresentable template (an unknown inType/outType byte, a non-reference length, or a malformed struct
            // member range) makes the writer return null, which yields an empty string.
            string template = source.Template is null
                ? string.Empty
                : WevtTemplateWriter.Write(source.Template.Nodes, source.Template.Descriptors) ?? string.Empty;

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
        ITraceLogger? logger)
    {
        RegistryProvider registryProvider = new(logger);
        IReadOnlyList<string> legacyFiles = registryProvider.GetMessageFilesForLegacyProvider(providerName);

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
