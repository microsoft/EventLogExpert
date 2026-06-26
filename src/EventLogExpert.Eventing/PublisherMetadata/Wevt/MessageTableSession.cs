// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;

namespace EventLogExpert.Eventing.PublisherMetadata.Wevt;

/// <summary>
///     Opens a provider's RT_MESSAGETABLE resources (MUI-aware) and resolves message ids to strings offline, without
///     EvtFormatMessage. Holds native module handles for its lifetime, so it is <see cref="IDisposable" />; the resolver
///     delegate must not be invoked after disposal.
/// </summary>
internal sealed class MessageTableSession : IDisposable
{
    private readonly string _providerName;
    private readonly List<(LibraryHandle Handle, nint Memory, uint Size)> _tables = [];

    private MessageTableSession(string providerName) => _providerName = providerName;

    public void Dispose()
    {
        foreach ((LibraryHandle handle, _, _) in _tables)
        {
            handle.Dispose();
        }

        _tables.Clear();
    }

    internal static MessageTableSession Open(string providerName, IEnumerable<string> candidateFiles, ITraceLogger? logger)
    {
        MessageTableSession session = new(providerName);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string file in candidateFiles)
        {
            if (string.IsNullOrEmpty(file) || !seen.Add(file))
            {
                continue;
            }

            if (MessageTableReader.TryOpen(file, logger, out LibraryHandle handle, out nint memory, out uint size))
            {
                session._tables.Add((handle, memory, size));
            }
        }

        return session;
    }

    internal string? Resolve(uint messageId)
    {
        if (messageId == uint.MaxValue)
        {
            return null;
        }

        // FindFirstByRawId reads block lowId/highId as signed Int32 and iterates them as long, so a message id with the
        // high bit set (common for WEVT: 0x9xxxxxxx / 0xDxxxxxxx) must be sign-extended to match. Zero-extending a uint
        // would compare a positive long against a negative block range and never hit.
        long signExtendedId = unchecked((int)messageId);

        foreach ((_, nint memory, uint size) in _tables)
        {
            MessageModel? model = MessageTableReader.FindFirstByRawId(memory, size, signExtendedId, _providerName);

            if (model is not null)
            {
                return model.Text;
            }
        }

        return null;
    }
}
