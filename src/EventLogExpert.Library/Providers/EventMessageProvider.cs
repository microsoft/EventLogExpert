// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;
using EventLogExpert.Library.Models;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;

namespace EventLogExpert.Library.Providers;

/// <summary>
///     Represents an event provider from a particular machine.
/// </summary>
public class EventMessageProvider
{
    private readonly string _providerName;
    private readonly RegistryProvider _registryProvider;
    private readonly Action<string> _traceAction;

    public EventMessageProvider(string providerName, Action<string> traceAction) : this(providerName,
        null,
        traceAction)
    { }

    public EventMessageProvider(string providerName, string computerName, Action<string> traceAction)
    {
        _providerName = providerName;
        _traceAction = s => { };
        _registryProvider = new RegistryProvider(computerName, _traceAction);
    }

    public ProviderDetails LoadProviderDetails()
    {
        var provider = LoadMessagesFromModernProvider();
        provider.Messages = LoadMessagesFromLegacyProvider().ToList();
        return provider;
    }

    /// <summary>
    ///     Loads the messages for a legacy provider from the files specified in
    ///     the registry. This information is stored at HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\EventLog
    /// </summary>
    /// <returns></returns>
    private IEnumerable<MessageModel> LoadMessagesFromLegacyProvider()
    {
        _traceAction($"LoadMessagesFromLegacyProvider called for provider {_providerName}");

        var legacyProviderFiles = _registryProvider.GetMessageFilesForLegacyProvider(_providerName);

        if (legacyProviderFiles == null)
        {
            _traceAction($"No message files found for provider {_providerName}. Returning 0 messages.");
            return new MessageModel[0];
        }

        var messages = new List<MessageModel>();

        foreach (var file in legacyProviderFiles)
        {
            var hModule = IntPtr.Zero;

            try
            {
                /*
                 * https://stackoverflow.com/questions/33498244/marshaling-a-message-table-resource
                 * 
                 * The approach documented there has some issues, so we deviate a bit.
                 * 
                 * RT_MESSAGETABLE is not found unless LoadLibraryEx is called with LOAD_LIBRARY_AS_DATAFILE.
                 * So we must use LoadLibraryEx below rather than LoadLibrary. Msedgeupdate.dll exposes this
                 * issue.
                 */

                hModule = NativeMethods.LoadLibraryEx(file, IntPtr.Zero, NativeMethods.LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE);

                // TODO: Evaulate if there's any need to EnumResourceTypes.
                // This is an alternative approach to FindResource. Leaving it here until we're sure FindResource is good enough.
                var result = NativeMethods.EnumResourceTypes(hModule, GetMessagesFromOneResource, IntPtr.Zero);

                var msgTableInfo =
                    NativeMethods.FindResource(hModule, 1, NativeMethods.RT_MESSAGETABLE);

                if (msgTableInfo == IntPtr.Zero)
                {
                    _traceAction($"No message table found. Returning 0 messages from file: {file}");
                    continue;
                }

                var msgTable = NativeMethods.LoadResource(hModule, msgTableInfo);
                var memTable = NativeMethods.LockResource(msgTable);

                var numberOfBlocks = Marshal.ReadInt32(memTable);
                var blockPtr = IntPtr.Add(memTable, 4);
                var blockSize = Marshal.SizeOf<NativeMethods.MESSAGE_RESOURCE_BLOCK>();

                for (var i = 0; i < numberOfBlocks; i++)
                {
                    var block = Marshal.PtrToStructure<NativeMethods.MESSAGE_RESOURCE_BLOCK>(blockPtr);
                    var entryPtr = IntPtr.Add(memTable, block.OffsetToEntries);

                    for (var id = block.LowId; id <= block.HighId; id++)
                    {
                        var length = Marshal.ReadInt16(entryPtr);
                        var flags = Marshal.ReadInt16(entryPtr, 2);
                        var textPtr = IntPtr.Add(entryPtr, 4);
                        string text;

                        if (flags == 0)
                        {
                            text = Marshal.PtrToStringAnsi(textPtr);
                        }
                        else if (flags == 1)
                        {
                            text = Marshal.PtrToStringUni(textPtr);
                        }
                        else if (flags == 2)
                        {
                            // All the ESE messages are a single-byte character set
                            // but have flags of 2, which is not defined. So just
                            // treat it as ANSI I guess?
                            text = Marshal.PtrToStringAnsi(textPtr);
                        }
                        else
                        {
                            text = "Error: Bad flags. Could not get text.";
                        }

                        // This is an event
                        messages.Add(new MessageModel
                        {
                            Text = text,
                            ShortId = (short)id,
                            ProviderName = _providerName,
                            RawId = id
                        });

                        // Advance to the next id
                        entryPtr = IntPtr.Add(entryPtr, length);
                    }

                    // Advance to the next block
                    blockPtr = IntPtr.Add(blockPtr, blockSize);
                }
            }
            catch (Exception ex)
            {
                // Hide the failure. We want to allow the results from the modern provider
                // to return even if we failed to load the legacy provider.
                _traceAction($"Failed to load legacy provider data for {_providerName}.");
                _traceAction(ex.ToString());
            }
            finally
            {
                if (hModule != IntPtr.Zero)
                {
                    NativeMethods.FreeLibrary(hModule);
                }
            }
        }

        _traceAction($"Returning {messages.Count} messages for provider {_providerName}");
        return messages;
    }

    private bool GetMessagesFromOneResource(IntPtr hModule, string lpszType, IntPtr lParam)
    {
        // No need to implement this as long as we can use FindResource instead.
        return true;
    }

    /// <summary>
    ///     Loads the messages for a modern provider. This info is stored at
    ///     Computer\HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\WINEVT
    /// </summary>
    /// <returns></returns>
    private ProviderDetails LoadMessagesFromModernProvider()
    {
        _traceAction($"LoadMessagesFromModernProvider called for provider {_providerName}");

        var provider = new ProviderDetails { ProviderName = _providerName };

        ProviderMetadata providerMetadata;
        try
        {
            providerMetadata = new ProviderMetadata(_providerName);
        }
        catch (Exception ex)
        {
            _traceAction($"Couldn't get metadata for rovider {_providerName}. Exception: {ex}. Returning empty Events list.");
            provider.Events = new List<EventModel>();
            return provider;
        }

        if (providerMetadata.Id == Guid.Empty)
        {
            _traceAction($"Provider {_providerName} has no provider GUID. Returning empty Events list.");
            provider.Events = new List<EventModel>();
            return provider;
        }

        provider.Events = providerMetadata.Events.Select(e => new EventModel
        {
            Description = e.Description,
            Id = e.Id,
            Keywords = e.Keywords.Select(k => k.Value).ToArray(),
            Level = e.Level.Value,
            LogName = e.LogLink.LogName,
            Opcode = e.Opcode.Value,
            Task = e.Task.Value,
            Version = e.Version,
            Template = e.Template
        }).ToList();

        provider.Keywords = providerMetadata.Keywords
            .Select(i => new ProviderDetails.ValueName { Value = i.Value, Name = i.DisplayName ?? i.Name })
            .ToList();

        provider.Opcodes = providerMetadata.Opcodes
            .Select(i => new ProviderDetails.ValueName { Value = i.Value, Name = i.DisplayName ?? i.Name })
            .ToList();

        provider.Tasks = providerMetadata.Tasks
            .Select(i => new ProviderDetails.ValueName { Value = i.Value, Name = i.DisplayName ?? i.Name })
            .ToList();

        _traceAction($"Returning {provider.Events?.Count} events for provider {_providerName}");
        return provider;
    }
}
