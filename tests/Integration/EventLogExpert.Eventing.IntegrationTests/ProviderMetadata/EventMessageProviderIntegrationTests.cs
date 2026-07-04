// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.ProviderMetadata;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.TestUtils.Constants;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using NSubstitute;
using System.Globalization;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.IntegrationTests.ProviderMetadata;

public sealed class EventMessageProviderIntegrationTests
{
    [Fact]
    public void LoadMessagesFromFiles_WhenBinaryUsesMuiSatellite_ShouldLoadMessagesFromMuiFile()
    {
        // wevtsvc.dll keeps messages in the .mui satellite; the loader must follow MUI fallback.
        var systemDirectory = Environment.SystemDirectory;
        var muiBinary = Path.Combine(systemDirectory, "wevtsvc.dll");

        Assert.SkipUnless(
            File.Exists(muiBinary) && TryFindMuiSatellite(systemDirectory, "wevtsvc.dll", out _),
            "Test requires wevtsvc.dll and a matching .mui satellite in the loader's MUI fallback chain (current UI culture or en-US).");

        var messages = EventMessageProvider.LoadMessagesFromFiles([muiBinary], Constants.TestProviderName);

        Assert.NotNull(messages);
        Assert.NotEmpty(messages);
        Assert.All(messages, m => Assert.Equal(Constants.TestProviderName, m.ProviderName));
    }

    [Fact]
    public void LoadMessagesFromFiles_WhenFilePathContainsEnvironmentVariable_ShouldHandleCorrectly()
    {
        // LoadLibraryEx does not expand env vars; provider paths must expand %SystemRoot% first.
        var systemDirectory = Environment.SystemDirectory;
        var muiBinary = Path.Combine(systemDirectory, "wevtsvc.dll");

        Assert.SkipUnless(
            File.Exists(muiBinary) && TryFindMuiSatellite(systemDirectory, "wevtsvc.dll", out _),
            "Test requires wevtsvc.dll and a matching .mui satellite in the loader's MUI fallback chain (current UI culture or en-US).");

        var filesWithEnvVar = new[] { @"%SystemRoot%\System32\wevtsvc.dll" };

        var messages = EventMessageProvider.LoadMessagesFromFiles(filesWithEnvVar, Constants.TestProviderName);

        Assert.NotNull(messages);
        Assert.NotEmpty(messages);
        Assert.All(messages, m => Assert.Equal(Constants.TestProviderName, m.ProviderName));
    }

    [Fact]
    public void LoadProviderDetails_ShouldLogProviderLoadingAttempt()
    {
        var mockLogger = Substitute.For<ITraceLogger>();
        EventMessageProvider provider = new(Constants.TestProviderName, logger: mockLogger);

        provider.LoadProviderDetails();

        mockLogger.Received().Debug(Arg.Any<DebugLogHandler>());
    }

    [Fact]
    public void LoadProviderDetails_WhenCalled_ShouldHaveNonNullCollections()
    {
        EventMessageProvider provider = new(Constants.TestProviderName);

        var details = provider.LoadProviderDetails();

        Assert.NotNull(details);
        Assert.NotNull(details.Events);
        Assert.NotNull(details.Keywords);
        Assert.NotNull(details.Opcodes);
        Assert.NotNull(details.Tasks);
        Assert.NotNull(details.Messages);
        Assert.NotNull(details.Parameters);
    }

    [Fact]
    public void LoadProviderDetails_WhenCalled_ShouldReturnProviderDetails()
    {
        EventMessageProvider provider = new(Constants.TestProviderName);

        var details = provider.LoadProviderDetails();

        Assert.NotNull(details);
        Assert.Equal(Constants.TestProviderName, details.ProviderName);
    }

    [Fact]
    public void LoadProviderDetails_WhenCalledMultipleTimes_ShouldReturnConsistentResults()
    {
        EventMessageProvider provider = new(Constants.TestProviderName);

        var details1 = provider.LoadProviderDetails();
        var details2 = provider.LoadProviderDetails();

        Assert.NotNull(details1);
        Assert.NotNull(details2);
        Assert.Equal(details1.ProviderName, details2.ProviderName);
    }

    [Fact]
    public void LoadProviderDetails_WhenChannelOwningPublisherUnknown_ShouldReturnEmptyDetailsWithoutFallback()
    {
        const string MadeUpName = "NonExistent-Publisher-And-Channel/Bogus";

        EventMessageProvider provider = new(MadeUpName);

        var details = provider.LoadProviderDetails();

        Assert.NotNull(details);
        Assert.True(details.IsEmpty);
        Assert.Null(details.ResolvedFromOwningPublisher);
        Assert.Equal(MadeUpName, details.ProviderName);
    }

    [Fact]
    public void LoadProviderDetails_WhenLegacyLoadReturnsNoMessages_ShouldFallBackToModernMessageFilePath()
    {
        Assert.SkipUnless(
            TryFindProviderWithEmptyLegacyAndWorkingModern(out var providerName),
            "Test requires a provider whose legacy registry entries load zero messages and whose modern publisher metadata exposes a loadable MessageFilePath. Common on dev machines but not guaranteed.");
        Assert.NotNull(providerName);

        var mockLogger = Substitute.For<ITraceLogger>();
        EventMessageProvider provider = new(providerName, logger: mockLogger);

        var details = provider.LoadProviderDetails();

        Assert.NotNull(details);
        Assert.NotEmpty(details.Messages);

        mockLogger.Received().Debug(Arg.Is<DebugLogHandler>(h =>
            h.ToString().Contains("No legacy messages loaded for provider") &&
            h.ToString().Contains("Using message file from modern provider")));
    }

    [Fact]
    public void LoadProviderDetails_WhenProviderHasNoData_ShouldReturnEmptyCollections()
    {
        EventMessageProvider provider = new(Constants.TestProviderName);

        var details = provider.LoadProviderDetails();

        Assert.NotNull(details);
        Assert.Empty(details.Messages);
        Assert.Empty(details.Parameters);
    }

    [Fact]
    public void LoadProviderDetails_WhenProviderNameIsActuallyAChannelPath_ShouldFallBackToOwningPublisher()
    {
        // Channel paths in ProviderName must resolve through EvtChannelConfigOwningPublisher.
        Assert.SkipUnless(
            TryFindChannelWithDistinctOwningPublisher(out var channelName, out var owningPublisher),
            "Test requires a registered channel whose owning publisher differs from the channel path itself.");
        Assert.NotNull(channelName);

        EventMessageProvider provider = new(channelName);

        var details = provider.LoadProviderDetails();

        Assert.NotNull(details);
        Assert.False(details.IsEmpty,
            $"Expected channel-owner fallback to populate metadata for channel '{channelName}' (owner '{owningPublisher}').");
        Assert.Equal(owningPublisher, details.ResolvedFromOwningPublisher);
    }

    [Fact]
    public void LoadProviderDetails_WhenProviderNotFound_ShouldReturnDetailsWithProviderName()
    {
        EventMessageProvider provider = new(Constants.TestProviderName);

        var details = provider.LoadProviderDetails();

        Assert.NotNull(details);
        Assert.Equal(Constants.TestProviderName, details.ProviderName);
    }

    [Fact]
    public void LoadProviderDetails_WhenStableProvider_ShouldResolveNamedValues()
    {
        // Modern metadata must resolve raw keyword/opcode/task message IDs into display names.
        EventMessageProvider provider = new(Constants.SecurityAuditingLogName);

        var details = provider.LoadProviderDetails();

        Assert.NotNull(details);
        Assert.SkipUnless(!details.IsEmpty, "Test requires the Microsoft-Windows-Security-Auditing provider on the host.");

        var resolvedNames = details.Keywords.Values
            .Concat(details.Opcodes.Values)
            .Concat(details.Tasks.Values);

        Assert.Contains(resolvedNames, name => !string.IsNullOrEmpty(name));
    }

    private static bool TryFindChannelWithDistinctOwningPublisher(out string? channelName, out string? owningPublisher)
    {
        foreach (var candidate in EventLogSession.GlobalSession.GetLogNames())
        {
            // Only modern channels carry an OwningPublisher distinct from the channel path.
            if (!candidate.Contains('/'))
            {
                continue;
            }

            using var channelConfig = NativeMethods.EvtOpenChannelConfig(
                EventLogSession.GlobalSession.Handle,
                candidate,
                0);

            if (channelConfig.IsInvalid)
            {
                continue;
            }

            if (!TryReadOwningPublisher(channelConfig, out var publisher) || string.IsNullOrEmpty(publisher))
            {
                continue;
            }

            if (string.Equals(publisher, candidate, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            channelName = candidate;
            owningPublisher = publisher;

            return true;
        }

        channelName = null;
        owningPublisher = null;

        return false;
    }

    private static bool TryFindMuiSatellite(string systemDirectory, string binaryName, out string? satellitePath)
    {
        var muiFileName = binaryName + ".mui";

        // Probe only the Win32 MUI loader order: current UI culture chain, then en-US.
        var probeOrder = new List<string>();

        for (var culture = CultureInfo.CurrentUICulture; !string.IsNullOrEmpty(culture.Name); culture = culture.Parent)
        {
            if (!probeOrder.Contains(culture.Name, StringComparer.OrdinalIgnoreCase))
            {
                probeOrder.Add(culture.Name);
            }
        }

        if (!probeOrder.Contains("en-US", StringComparer.OrdinalIgnoreCase))
        {
            probeOrder.Add("en-US");
        }

        foreach (var cultureName in probeOrder)
        {
            var candidate = Path.Combine(systemDirectory, cultureName, muiFileName);

            if (!File.Exists(candidate)) { continue; }

            satellitePath = candidate;

            return true;
        }

        satellitePath = null;

        return false;
    }

    private static bool TryFindProviderWithEmptyLegacyAndWorkingModern(out string? providerName)
    {
        var registry = new RegistryProvider();

        foreach (var candidate in EventLogSession.GlobalSession.GetProviderNames())
        {
            var legacyFiles = registry.GetMessageFilesForLegacyProvider(candidate).ToList();

            if (legacyFiles.Count == 0)
            {
                continue;
            }

            // Legacy registry paths can survive uninstall but load zero messages.
            var legacyMessages = EventMessageProvider.LoadMessagesFromFiles(legacyFiles, candidate);

            if (legacyMessages.Count > 0)
            {
                continue;
            }

            PublisherMetadataHandle? metadata;

            try
            {
                metadata = PublisherMetadataHandle.Create(candidate);
            }
            catch
            {
                continue;
            }

            using (metadata)
            {
                if (metadata is null || string.IsNullOrEmpty(metadata.MessageFilePath))
                {
                    continue;
                }

                // Require modern metadata to load messages; otherwise the fallback would also be empty.
                var modernMessages = EventMessageProvider.LoadMessagesFromFiles([metadata.MessageFilePath], candidate);

                if (modernMessages.Count == 0)
                {
                    continue;
                }

                providerName = candidate;

                return true;
            }
        }

        providerName = null;

        return false;
    }

    private static bool TryReadOwningPublisher(EvtHandle channelConfig, out string? publisher)
    {
        publisher = null;

        bool success = NativeMethods.EvtGetChannelConfigProperty(
            channelConfig,
            EvtChannelConfigPropertyId.EvtChannelConfigOwningPublisher,
            0,
            0,
            IntPtr.Zero,
            out int bufferSize);

        int error = Marshal.GetLastWin32Error();

        if (!success && error != Win32ErrorCodes.ERROR_INSUFFICIENT_BUFFER)
        {
            return false;
        }

        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);

        try
        {
            success = NativeMethods.EvtGetChannelConfigProperty(
                channelConfig,
                EvtChannelConfigPropertyId.EvtChannelConfigOwningPublisher,
                0,
                bufferSize,
                buffer,
                out _);

            if (!success)
            {
                return false;
            }

            var variant = Marshal.PtrToStructure<EvtVariant>(buffer);
            publisher = NativeMethods.ConvertVariant(variant) as string;

            return !string.IsNullOrEmpty(publisher);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
