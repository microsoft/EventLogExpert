using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Runtime.InteropServices;

namespace EventLogExpert.EventUtils
{
    /// <summary>
    ///     Represents an event provider from a particular machine.
    /// </summary>
    public class EventMessageProvider
    {
        private readonly string _providerName;
        private readonly RegistryProvider _registryProvider;
        private readonly Action<string> _traceAction;

        public EventMessageProvider(string providerName, Action<string> traceAction) : this(providerName, null,
            traceAction)
        {
        }

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
        private IEnumerable<Message> LoadMessagesFromLegacyProvider()
        {
            _traceAction($"LoadMessagesFromLegacyProvider called for provider {_providerName}");

            var legacyProviderFiles = _registryProvider.GetMessageFilesForLegacyProvider(_providerName);

            if (legacyProviderFiles == null)
            {
                _traceAction($"No message files found for provider {_providerName}. Returning 0 messages.");
                return new Message[0];
            }

            var messages = new List<Message>();
            foreach (var file in legacyProviderFiles)
            {
                var hModule = IntPtr.Zero;

                try
                {
                    // https://stackoverflow.com/questions/33498244/marshaling-a-message-table-resource
                    hModule = NativeMethods.LoadLibrary(file);
                    var msgTableInfo =
                        NativeMethods.FindResource(hModule, 1, NativeMethods.RT_MESSAGETABLE);
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
                                text = Marshal.PtrToStringAnsi(textPtr);
                            else if (flags == 1)
                                text = Marshal.PtrToStringUni(textPtr);
                            else
                                text = "Error: Bad flags. Could not get text.";

                            // This is an event
                            messages.Add(new Message
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

        /// <summary>
        ///     Loads the messages for a modern provider. This info is stored at
        ///     Computer\HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\WINEVT
        /// </summary>
        /// <returns></returns>
        private ProviderDetails LoadMessagesFromModernProvider()
        {
            _traceAction($"LoadMessagesFromModernProvider called for provider {_providerName}");

            var provider = new ProviderDetails { ProviderName = _providerName };

            var providerMetadata = new ProviderMetadata(_providerName);

            if (providerMetadata.Id == Guid.Empty)
            {
                _traceAction($"Provider {_providerName} has no provider GUID. Returning empty provider.");
                return provider;
            }

            provider.Events = providerMetadata.Events.Select(e => new Event
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
}
