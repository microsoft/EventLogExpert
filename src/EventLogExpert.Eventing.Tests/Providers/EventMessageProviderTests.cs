// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Eventing.Models;
using EventLogExpert.Eventing.Providers;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Tests.TestUtils.Constants;
using NSubstitute;
using System.Globalization;
using System.Runtime.InteropServices;

namespace EventLogExpert.Eventing.Tests.Providers;

public sealed class EventMessageProviderTests
{
    [Theory]
    [InlineData(Constants.TestProviderName)]
    [InlineData(Constants.TestProviderLongName)]
    public void Constructor_WhenDifferentProviderNames_ShouldCreateInstances(string providerName)
    {
        // Arrange & Act
        EventMessageProvider provider = new(providerName);

        // Assert
        Assert.NotNull(provider);
    }

    [Fact]
    public void GetMessages_WhenBinaryUsesMuiSatellite_ShouldLoadMessagesFromMuiFile()
    {
        // wevtsvc.dll on Windows keeps its message table in <locale>\wevtsvc.dll.mui rather
        // than in the binary itself. The previous load path used LOAD_LIBRARY_AS_DATAFILE
        // with only the leaf filename and therefore failed with ERROR_RESOURCE_TYPE_NOT_FOUND
        // (1813) for binaries like this — including the WMIRegistrationService.exe scenario
        // that motivated this fix. The MUI-aware load path should resolve them.
        var systemDirectory = Environment.SystemDirectory;
        var muiBinary = Path.Combine(systemDirectory, "wevtsvc.dll");

        Assert.SkipUnless(
            File.Exists(muiBinary) && TryFindMuiSatellite(systemDirectory, "wevtsvc.dll", out _),
            "Test requires wevtsvc.dll and a matching .mui satellite in the loader's MUI fallback chain (current UI culture or en-US).");

        // Act
        var messages = EventMessageProvider.GetMessages([muiBinary], Constants.TestProviderName);

        // Assert
        Assert.NotNull(messages);
        Assert.NotEmpty(messages);
        Assert.All(messages, m => Assert.Equal(Constants.TestProviderName, m.ProviderName));
    }

    [Fact]
    public void GetMessages_WhenDuplicateFiles_ShouldProcessAll()
    {
        // Arrange
        var duplicateFiles = new[] { Constants.NonExistentDll, Constants.NonExistentDll };
        var mockLogger = Substitute.For<ITraceLogger>();

        // Act
        var messages = EventMessageProvider.GetMessages(duplicateFiles, Constants.TestProviderName, mockLogger);

        // Assert
        Assert.NotNull(messages);

        // Each input that fails the primary MUI-aware load produces a debug log that begins with
        // "LoadLibraryEx failed for {file}". Asserting per-input presence (with the filename in
        // the message) is robust to future changes in the number of fallback attempts or extra
        // diagnostic lines per input — only the primary-attempt failure log is contractually
        // guaranteed to fire once per input here.
        mockLogger.Received(duplicateFiles.Length)
            .Debug(Arg.Is<DebugLogHandler>(h =>
                h.ToString().Contains("LoadLibraryEx failed") &&
                h.ToString().Contains(Constants.NonExistentDll)));
    }

    [Fact]
    public void GetMessages_WhenEmptyFileList_ShouldReturnEmptyList()
    {
        // Arrange & Act
        var messages = EventMessageProvider.GetMessages([], Constants.TestProviderName);

        // Assert
        Assert.NotNull(messages);
        Assert.Empty(messages);
    }

    [Fact]
    public void GetMessages_WhenFileListIsNull_ShouldThrowNullReferenceException()
    {
        // Arrange
        var mockLogger = Substitute.For<ITraceLogger>();

        // Act & Assert
        Assert.Throws<NullReferenceException>(() =>
            EventMessageProvider.GetMessages(null!, Constants.TestProviderName, mockLogger));
    }

    [Fact]
    public void GetMessages_WhenFilePathContainsEnvironmentVariable_ShouldHandleCorrectly()
    {
        // LoadLibraryEx does not expand environment variables. Channel-named providers like
        // Microsoft-Windows-AppXDeploymentServer/Operational supply paths such as
        // "%SystemRoot%\system32\AppXDeploymentServer.dll" through the publisher manifest;
        // EventMessageProvider must expand them defensively before calling LoadLibraryEx,
        // otherwise the load fails with ERROR_FILE_NOT_FOUND. We exercise the same path
        // here with wevtsvc.dll, which is present on every Windows host.
        var systemDirectory = Environment.SystemDirectory;
        var muiBinary = Path.Combine(systemDirectory, "wevtsvc.dll");

        Assert.SkipUnless(
            File.Exists(muiBinary) && TryFindMuiSatellite(systemDirectory, "wevtsvc.dll", out _),
            "Test requires wevtsvc.dll and a matching .mui satellite in the loader's MUI fallback chain (current UI culture or en-US).");

        var filesWithEnvVar = new[] { @"%SystemRoot%\System32\wevtsvc.dll" };

        var messages = EventMessageProvider.GetMessages(filesWithEnvVar, Constants.TestProviderName);

        Assert.NotNull(messages);
        Assert.NotEmpty(messages);
        Assert.All(messages, m => Assert.Equal(Constants.TestProviderName, m.ProviderName));
    }

    [Fact]
    public void GetMessages_WhenFilePathHasMultipleBackslashes_ShouldExtractFileName()
    {
        // Arrange
        var filesWithPath = new[] { Constants.NonExistentDllFullPath };

        // Act
        var messages = EventMessageProvider.GetMessages(filesWithPath, Constants.TestProviderName);

        // Assert
        Assert.NotNull(messages);
    }

    [Fact]
    public void GetMessages_WhenInvalidFile_ShouldLogWarning()
    {
        // Arrange
        var invalidFiles = new[] { Constants.NonExistentDll };
        var mockLogger = Substitute.For<ITraceLogger>();

        // Act
        EventMessageProvider.GetMessages(invalidFiles, Constants.TestProviderName, mockLogger);

        // Assert: an unresolvable path produces a LoadLibraryEx failure log for both the
        // MUI-aware primary attempt and the leaf-name fallback.
        mockLogger.Received()
            .Debug(Arg.Is<DebugLogHandler>(h =>
                h.ToString().Contains("LoadLibraryEx failed") &&
                h.ToString().Contains("LOAD_LIBRARY_AS_IMAGE_RESOURCE")));

        mockLogger.Received()
            .Debug(Arg.Is<DebugLogHandler>(h =>
                h.ToString().Contains("LoadLibraryEx failed") &&
                h.ToString().Contains("leaf-name fallback")));
    }

    [Fact]
    public void GetMessages_WhenInvalidFile_ShouldReturnEmptyList()
    {
        // Arrange
        var invalidFiles = new[] { Constants.NonExistentDll };

        // Act
        var messages = EventMessageProvider.GetMessages(invalidFiles, Constants.TestProviderName);

        // Assert
        Assert.NotNull(messages);
        Assert.Empty(messages);
    }

    [Fact]
    public void GetMessages_WhenMultipleInvalidFiles_ShouldLogMultipleWarnings()
    {
        // Arrange
        var invalidFiles = new[] { Constants.NonExistentDll, Constants.NonExistentDll };
        var mockLogger = Substitute.For<ITraceLogger>();

        // Act
        EventMessageProvider.GetMessages(invalidFiles, Constants.TestProviderName, mockLogger);

        // Assert: each input that fails the primary MUI-aware load produces a debug log that
        // begins with "LoadLibraryEx failed for {file}". Asserting per-input presence (with the
        // filename in the message) is robust to future changes in the number of fallback attempts
        // or extra diagnostic lines per input — only the primary-attempt failure log is
        // contractually guaranteed to fire once per input here.
        mockLogger.Received(invalidFiles.Length)
            .Debug(Arg.Is<DebugLogHandler>(h =>
                h.ToString().Contains("LoadLibraryEx failed") &&
                h.ToString().Contains(Constants.NonExistentDll)));
    }

    [Fact]
    public void GetMessages_WhenMultipleInvalidFiles_ShouldReturnEmptyList()
    {
        // Arrange
        var invalidFiles = new[] { Constants.NonExistentDll, Constants.NonExistentDll, Constants.NonExistentDll };

        // Act
        var messages = EventMessageProvider.GetMessages(invalidFiles, Constants.TestProviderName);

        // Assert
        Assert.NotNull(messages);
        Assert.Empty(messages);
    }

    [Fact]
    public void GetMessages_WhenProviderNameProvided_ShouldIncludeInMessages()
    {
        // Arrange & Act
        var messages = EventMessageProvider.GetMessages([], Constants.TestProviderName);

        // Assert
        Assert.NotNull(messages);
    }

    [Fact]
    public void LoadProviderDetails_ShouldLogProviderLoadingAttempt()
    {
        // Arrange
        var mockLogger = Substitute.For<ITraceLogger>();
        EventMessageProvider provider = new(Constants.TestProviderName, logger: mockLogger);

        // Act
        provider.LoadProviderDetails();

        // Assert
        mockLogger.Received().Debug(Arg.Any<DebugLogHandler>());
    }

    [Fact]
    public void LoadProviderDetails_WhenCalled_ShouldHaveNonNullCollections()
    {
        // Arrange
        EventMessageProvider provider = new(Constants.TestProviderName);

        // Act
        var details = provider.LoadProviderDetails();

        // Assert
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
        // Arrange
        EventMessageProvider provider = new(Constants.TestProviderName);

        // Act
        var details = provider.LoadProviderDetails();

        // Assert
        Assert.NotNull(details);
        Assert.Equal(Constants.TestProviderName, details.ProviderName);
    }

    [Fact]
    public void LoadProviderDetails_WhenCalledMultipleTimes_ShouldReturnConsistentResults()
    {
        // Arrange
        EventMessageProvider provider = new(Constants.TestProviderName);

        // Act
        var details1 = provider.LoadProviderDetails();
        var details2 = provider.LoadProviderDetails();

        // Assert
        Assert.NotNull(details1);
        Assert.NotNull(details2);
        Assert.Equal(details1.ProviderName, details2.ProviderName);
    }

    [Fact]
    public void LoadProviderDetails_WhenChannelOwningPublisherUnknown_ShouldReturnEmptyDetailsWithoutFallback()
    {
        // A made-up provider name that is neither a registered publisher nor a registered channel.
        // The owning-publisher probe must return false and the resolver must end up with an empty
        // ProviderDetails instead of recursing or throwing.
        const string MadeUpName = "NonExistent-Publisher-And-Channel/Bogus";

        EventMessageProvider provider = new(MadeUpName);

        var details = provider.LoadProviderDetails();

        Assert.NotNull(details);
        Assert.True(details.IsEmpty);
        Assert.Null(details.ResolvedFromOwningPublisher);
        Assert.Equal(MadeUpName, details.ProviderName);
    }

    [Fact]
    public void LoadProviderDetails_WhenProviderHasNoData_ShouldReturnEmptyCollections()
    {
        // Arrange
        EventMessageProvider provider = new(Constants.TestProviderName);

        // Act
        var details = provider.LoadProviderDetails();

        // Assert
        Assert.NotNull(details);
        Assert.Empty(details.Messages);
        Assert.Empty(details.Parameters);
    }

    [Fact]
    public void LoadProviderDetails_WhenProviderNameIsActuallyAChannelPath_ShouldFallBackToOwningPublisher()
    {
        // Some events arrive with a channel path in the ProviderName slot rather than the real
        // publisher name (the AppXDeploymentServer/Operational case). We expect the fallback to
        // resolve via EvtChannelConfigOwningPublisher and load the real publisher's metadata.
        Assert.SkipUnless(
            TryFindChannelWithDistinctOwningPublisher(out var channelName, out var owningPublisher),
            "Test requires a registered channel whose owning publisher differs from the channel path itself.");

        EventMessageProvider provider = new(channelName!);

        var details = provider.LoadProviderDetails();

        Assert.NotNull(details);
        Assert.False(details.IsEmpty,
            $"Expected channel-owner fallback to populate metadata for channel '{channelName}' (owner '{owningPublisher}').");
        Assert.Equal(owningPublisher, details.ResolvedFromOwningPublisher);
    }

    [Fact]
    public void LoadProviderDetails_WhenProviderNotFound_ShouldReturnDetailsWithProviderName()
    {
        // Arrange
        EventMessageProvider provider = new(Constants.TestProviderName);

        // Act
        var details = provider.LoadProviderDetails();

        // Assert
        Assert.NotNull(details);
        Assert.Equal(Constants.TestProviderName, details.ProviderName);
    }

    private static bool TryFindChannelWithDistinctOwningPublisher(out string? channelName, out string? owningPublisher)
    {
        foreach (var candidate in EventLogSession.GlobalSession.GetLogNames())
        {
            // Only modern channels carry an OwningPublisher distinct from the channel path itself.
            if (!candidate.Contains('/'))
            {
                continue;
            }

            using var channelConfig = EventMethods.EvtOpenChannelConfig(
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

        // Only probe locales the Win32 MUI loader will actually consult: the current UI culture,
        // its parents, and "en-US" as the well-known system fallback that ships with every Windows
        // SKU. A satellite present in some other locale subfolder would pass a broad existence
        // check but the loader would never select it, so the test could still fail after skipping.
        // The loop terminates naturally when culture.Name becomes empty (the invariant culture).
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

    private static bool TryReadOwningPublisher(EvtHandle channelConfig, out string? publisher)
    {
        publisher = null;

        bool success = EventMethods.EvtGetChannelConfigProperty(
            channelConfig,
            EvtChannelConfigPropertyId.EvtChannelConfigOwningPublisher,
            0,
            0,
            IntPtr.Zero,
            out int bufferSize);

        int error = Marshal.GetLastWin32Error();

        if (!success && error != Interop.ERROR_INSUFFICIENT_BUFFER)
        {
            return false;
        }

        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);

        try
        {
            success = EventMethods.EvtGetChannelConfigProperty(
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
            publisher = EventMethods.ConvertVariant(variant) as string;

            return !string.IsNullOrEmpty(publisher);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
