// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;

namespace EventLogExpert.Eventing.PublisherMetadata;

/// <summary>
///     Builds a <see cref="ProviderDetails" /> from source-agnostic <see cref="RawProviderContent" />. The native
///     publisher-metadata path feeds this today; the offline WEVT parser feeds the same assembler later, so a single
///     resolution + projection path serves both and the two cannot drift. Each section is assembled under its own
///     try/catch so a resolution failure empties only that section, matching the per-section behavior of the original
///     inline load path.
/// </summary>
internal static class ProviderDetailsAssembler
{
    internal static ProviderDetails Assemble(RawProviderContent content, ITraceLogger? logger)
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

        PopulateValueMaps(provider, content, logger);

        return provider;
    }

    internal static string InjectMapAttribute(string template, string fieldName, string mapName)
    {
        string nameAttribute = $"name=\"{fieldName}\"";
        int searchStart = 0;

        while (true)
        {
            int dataIndex = template.IndexOf("<data", searchStart, StringComparison.OrdinalIgnoreCase);

            if (dataIndex < 0) { return template; }

            int afterTag = dataIndex + "<data".Length;
            char delimiter = afterTag < template.Length ? template[afterTag] : '\0';

            // "<data" prefixes "<dataSource"; only an element whose tag ends here is a real <data> node.
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
                return template.Insert(nameIndex + nameAttribute.Length, $" map=\"{mapName}\"");
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
                // No-message events resolve to string.Empty (not null) to match the native path; the encoder hashes
                // null and empty differently.
                Description = raw.MessageId == uint.MaxValue ? string.Empty : content.ResolveMessage(raw.MessageId) ?? string.Empty,
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
            // Message-id wins over the inline name when a real id exists (mirrors the native getters); the resolver
            // coalesce only guards the offline message-table resolver, which can return null - native never does. The
            // inline name is preserved as-is (the native getters TryAdd the raw name, which may be null/empty).
            string? name = entry.MessageId == uint.MaxValue
                ? entry.InlineName
                : resolveMessage(entry.MessageId) ?? string.Empty;

            dictionary.TryAdd(keyProjector(entry.Value), name!);
        }

        return dictionary;
    }

    /// <summary>Expands a u64 keyword mask MSB-first into individual set-bit values, matching the live event Keywords.</summary>
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

    private static void PopulateValueMaps(ProviderDetails provider, RawProviderContent content, ITraceLogger? logger)
    {
        try
        {
            if (content.PublisherGuid == Guid.Empty) { return; }

            if (string.IsNullOrEmpty(content.ResourceFilePath)) { return; }

            WevtTemplateData? templateData = WevtTemplateReader.TryRead(content.ResourceFilePath, content.PublisherGuid, logger);

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
}
