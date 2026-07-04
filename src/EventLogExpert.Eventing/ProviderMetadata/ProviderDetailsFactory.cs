// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.ProviderMetadata.Wevt;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;

namespace EventLogExpert.Eventing.ProviderMetadata;

internal static class ProviderDetailsFactory
{
    internal static ProviderDetails Create(RawProviderContent content, ITraceLogger? logger) =>
        Create(content, preParsedTemplates: null, logger);

    internal static ProviderDetails Create(
        RawProviderContent content,
        WevtTemplateData? preParsedTemplates,
        ITraceLogger? logger)
    {
        var provider = new ProviderDetails { ProviderName = content.ProviderName };

        try
        {
            provider.Events = BuildEvents(content);
        }
        catch (Exception ex)
        {
            logger?.Debug($"Failed to load Events for modern provider: {content.ProviderName}. Exception:\n{ex}");
        }

        try
        {
            provider.Keywords = BuildNamedDictionary(content.Keywords, content.ResolveMessage, static value => unchecked((long)value));
        }
        catch (Exception ex)
        {
            logger?.Debug($"Failed to load Keywords for modern provider: {content.ProviderName}. Exception:\n{ex}");
        }

        try
        {
            provider.Opcodes = BuildNamedDictionary(content.Opcodes, content.ResolveMessage, static value => unchecked((int)((uint)value >> 16)));
        }
        catch (Exception ex)
        {
            logger?.Debug($"Failed to load Opcodes for modern provider: {content.ProviderName}. Exception:\n{ex}");
        }

        try
        {
            provider.Tasks = BuildNamedDictionary(content.Tasks, content.ResolveMessage, static value => unchecked((int)(uint)value));
        }
        catch (Exception ex)
        {
            logger?.Debug($"Failed to load Tasks for modern provider: {content.ProviderName}. Exception:\n{ex}");
        }

        PopulateValueMaps(provider, content, preParsedTemplates, logger);

        return provider;
    }

    internal static string InjectMapAttribute(string template, string fieldName, string mapName)
    {
        // Match the escaped emitted name= attribute so XML-special field names are still found.
        string nameAttribute = $"name=\"{WevtTemplateWriter.EscapeXmlAttribute(fieldName)}\"";
        int searchStart = 0;

        while (true)
        {
            int dataIndex = template.IndexOf("<data", searchStart, StringComparison.OrdinalIgnoreCase);

            if (dataIndex < 0) { return template; }

            int afterTag = dataIndex + "<data".Length;
            char delimiter = afterTag < template.Length ? template[afterTag] : '\0';

            // Reject <dataSource>; only a tag boundary means this is a real <data> element.
            if (delimiter is not (' ' or '\t' or '\r' or '\n' or '>' or '/'))
            {
                searchStart = afterTag;

                continue;
            }

            int elementEnd = template.IndexOf('>', afterTag);

            if (elementEnd < 0) { return template; }

            int nameIndex = template.IndexOf(nameAttribute, dataIndex, StringComparison.OrdinalIgnoreCase);

            if (nameIndex >= 0 && nameIndex < elementEnd)
            {
                return template.Insert(nameIndex + nameAttribute.Length, $" map=\"{WevtTemplateWriter.EscapeXmlAttribute(mapName)}\"");
            }

            searchStart = elementEnd + 1;
        }
    }

    private static EventModel[] BuildEvents(RawProviderContent content)
    {
        var events = new EventModel[content.Events.Count];

        for (int i = 0; i < content.Events.Count; i++)
        {
            RawProviderEvent raw = content.Events[i];

            events[i] = new EventModel
            {
                // No-message events use empty string, not null, to match native-path hashing.
                Description = raw.MessageId == uint.MaxValue
                    ? string.Empty
                    : content.ResolveMessage(raw.MessageId)?.TrimEnd('\0', '\r', '\n', '\t', ' ') ?? string.Empty,
                Id = raw.Id,
                Keywords = ExpandKeywords(raw.KeywordsMask),
                Level = raw.Level,
                LogName = content.Channels.GetValueOrDefault(raw.ChannelId),
                Opcode = raw.Opcode,
                Task = raw.Task,
                Template = raw.Template,
                Version = raw.Version
            };
        }

        return events;
    }

    private static Dictionary<TKey, string> BuildNamedDictionary<TKey>(
        IReadOnlyList<RawNamedValue> entries,
        Func<uint, string?> resolveMessage,
        Func<ulong, TKey> keyProjector)
        where TKey : notnull
    {
        var dictionary = new Dictionary<TKey, string>(entries.Count);

        foreach (RawNamedValue entry in entries)
        {
            // Message-id names win to match native getters; null only guards the offline resolver.
            string? resolvedName = entry.MessageId == uint.MaxValue
                ? entry.InlineName
                : resolveMessage(entry.MessageId) ?? string.Empty;

            // Trim source-specific trailing controls so native and offline names share VersionKey values.
            dictionary.TryAdd(keyProjector(entry.Value), resolvedName?.TrimEnd('\0', '\r', '\n', '\t', ' ') ?? string.Empty);
        }

        return dictionary;
    }

    // Expand keyword masks MSB-first to match live event Keywords.
    private static long[] ExpandKeywords(ulong keywordsMask)
    {
        List<long> keywords = [];

        ulong mask = 0x8000000000000000;

        for (int i = 0; i < 64; i++)
        {
            if ((keywordsMask & mask) > 0)
            {
                keywords.Add(unchecked((long)mask));
            }

            mask >>= 1;
        }

        return keywords.ToArray();
    }

    private static void InjectMapAttributes(
        IReadOnlyList<EventModel> events,
        IReadOnlyDictionary<WevtEventKey, IReadOnlyDictionary<string, string>> eventFieldMaps,
        IReadOnlyDictionary<string, ValueMapDefinition> decodedMaps)
    {
        if (eventFieldMaps.Count == 0) { return; }

        foreach (EventModel model in events)
        {
            if (string.IsNullOrEmpty(model.Template)) { continue; }

            if (!eventFieldMaps.TryGetValue(
                    new WevtEventKey((uint)model.Id, model.Version),
                    out IReadOnlyDictionary<string, string>? fieldMaps))
            {
                continue;
            }

            string template = model.Template;

            foreach ((string fieldName, string mapName) in fieldMaps)
            {
                if (decodedMaps.ContainsKey(mapName))
                {
                    template = InjectMapAttribute(template, fieldName, mapName);
                }
            }

            model.Template = template;
        }
    }

    private static void PopulateValueMaps(
        ProviderDetails provider,
        RawProviderContent content,
        WevtTemplateData? preParsed,
        ITraceLogger? logger)
    {
        try
        {
            // Offline supplies pre-parsed maps; native reads lazily, then both share map resolution and injection.
            WevtTemplateData? templateData = preParsed ?? TryReadTemplateData(content, logger);

            if (templateData is null || templateData.Maps.Count == 0) { return; }

            Dictionary<string, ValueMapDefinition> decodedMaps = new(StringComparer.Ordinal);

            foreach ((string mapName, WevtRawMap rawMap) in templateData.Maps)
            {
                ValueMapDefinition? definition = ResolveMap(rawMap, content.ResolveMessage, content.ProviderName, logger);

                if (definition is not null)
                {
                    decodedMaps[mapName] = definition;
                }
            }

            if (decodedMaps.Count == 0) { return; }

            provider.Maps = decodedMaps;

            InjectMapAttributes(provider.Events, templateData.EventFieldMaps, decodedMaps);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException
                                       and not StackOverflowException
                                       and not AccessViolationException)
        {
            logger?.Debug($"Failed to populate value maps for modern provider: {content.ProviderName}. Exception:\n{ex}");
        }
    }

    private static ValueMapDefinition? ResolveMap(
        WevtRawMap rawMap,
        Func<uint, string?> resolveMessage,
        string providerName,
        ITraceLogger? logger)
    {
        List<ValueMapEntry> entries = new(rawMap.Entries.Count);

        foreach (WevtRawMapEntry entry in rawMap.Entries)
        {
            if (entry.MessageId == uint.MaxValue) { continue; }

            string? name;

            try
            {
                name = resolveMessage(entry.MessageId);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException
                                           and not StackOverflowException
                                           and not AccessViolationException)
            {
                logger?.Debug($"Failed to resolve map message {entry.MessageId} for provider {providerName}: {ex.Message}");

                continue;
            }

            if (string.IsNullOrEmpty(name)) { continue; }

            entries.Add(new ValueMapEntry(entry.Value, name.TrimEnd('\0', '\r', '\n', '\t', ' ')));
        }

        return entries.Count > 0 ? new ValueMapDefinition(rawMap.IsBitMap, entries) : null;
    }

    private static WevtTemplateData? TryReadTemplateData(RawProviderContent content, ITraceLogger? logger)
    {
        if (content.PublisherGuid == Guid.Empty) { return null; }

        if (string.IsNullOrEmpty(content.ResourceFilePath)) { return null; }

        return WevtTemplateReader.TryRead(content.ResourceFilePath, content.PublisherGuid, logger);
    }
}
