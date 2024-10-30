// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Models;

internal readonly record struct EventMetadata
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

    public string Description { get; }

    public long Id { get; }

    public IEnumerable<long> Keywords
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

    public byte Level { get; }

    public string LogName => _provider.Channels[_channelId];

    public int Opcode { get; }

    public int Task { get; }

    public string Template { get; }

    public byte Version { get; }
}
