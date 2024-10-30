// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Providers;

namespace EventLogExpert.Eventing.Models;

internal sealed record EventMetadata
{
    private readonly byte _channelId;
    private readonly long _keywords;
    private readonly ProviderMetadata _provider;

    internal EventMetadata(
        uint id,
        byte version,
        byte channelId,
        byte level,
        byte opcode,
        short task,
        long keywords,
        string template,
        string description,
        ProviderMetadata provider)
    {
        Id = id;
        Version = version;
        _channelId = channelId;
        Level = level;
        Opcode = opcode;
        Task = task;
        _keywords = keywords;
        Template = template;
        Description = description;
        _provider = provider;
    }

    internal string Description { get; }

    internal long Id { get; }

    internal IEnumerable<long> Keywords
    {
        get
        {
            List<long> keywords = [];

            ulong mask = 0x8000000000000000;

            for (int i = 0; i < 64; i++)
            {
                if (((ulong)_keywords & mask) > 0)
                {
                    keywords.Add(unchecked((long)mask));
                }

                mask >>= 1;
            }

            return keywords;
        }
    }

    internal byte Level { get; }

    internal string? LogName
    {
        get
        {
            _provider.Channels.TryGetValue(_channelId, out string? logName);

            return logName;
        }
    }

    internal int Opcode { get; }

    internal int Task { get; }

    internal string Template { get; }

    internal byte Version { get; }
}
