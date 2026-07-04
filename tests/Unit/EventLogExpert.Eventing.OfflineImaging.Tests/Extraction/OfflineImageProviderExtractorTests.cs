// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.OfflineImaging;
using EventLogExpert.Eventing.OfflineImaging.Extraction;
using EventLogExpert.Eventing.ProviderMetadata;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace EventLogExpert.Eventing.OfflineImaging.Tests.Extraction;

public sealed class OfflineImageProviderExtractorTests
{
    private const string PublishersKeyPath = @"Microsoft\Windows\CurrentVersion\WINEVT\Publishers";
    private const string TestGuid = "{33333333-3333-3333-3333-333333333333}";

    [Fact]
    public void ReadImageProvenance_DoesNotExpandRegExpandSzValuesAgainstTheHost()
    {
        using OfflineTestImage image = OfflineTestImage.Create(
            software =>
            {
                using RegistryKey currentVersion = software.CreateSubKey(@"Microsoft\Windows NT\CurrentVersion");
                currentVersion.SetValue("CurrentBuildNumber", "20348", RegistryValueKind.String);

                // REG_EXPAND_SZ provenance must stay literal and never expand against the host environment.
                currentVersion.SetValue("EditionID", @"%SystemRoot%Edition", RegistryValueKind.ExpandString);
                currentVersion.SetValue("DisplayVersion", @"%SystemRoot%", RegistryValueKind.ExpandString);
            },
            SeedSystem);
        using OfflineImageProviderExtractor extractor = OfflineImageProviderExtractor.TryCreate(image.ImageRoot, logger: null)!;

        SourceOsProvenance provenance = extractor.ReadImageProvenance();

        Assert.Equal(20348, provenance.Build);
        Assert.Equal(@"%SystemRoot%Edition", provenance.Edition);
        Assert.Equal(@"%SystemRoot%", provenance.DisplayVersion);
    }

    [Fact]
    public void ReadImageProvenance_ReadsCurrentVersionFromTheImageSoftwareHive()
    {
        using OfflineTestImage image = OfflineTestImage.Create(SeedSoftware, SeedSystem);
        using OfflineImageProviderExtractor extractor = OfflineImageProviderExtractor.TryCreate(image.ImageRoot, logger: null)!;

        SourceOsProvenance provenance = extractor.ReadImageProvenance();

        Assert.Equal(20348, provenance.Build);
        Assert.Equal(2700, provenance.Revision);
        Assert.Equal("ServerDatacenter", provenance.Edition);
        Assert.Equal("21H2", provenance.DisplayVersion);
    }

    [Fact]
    public void ReadImageProvenance_WhenCurrentVersionIsAbsent_LogsUnderTheProvidersCategory_NotTheHiveCategory()
    {
        // The semantic "provenance unavailable" message belongs to Offline.Providers; only the raw regf parser
        // (OfflineHiveFile) is Offline.Hive. This locks the Providers-vs-Hive attribution split.
        using OfflineTestImage image = OfflineTestImage.Create(seedSoftware: _ => { }, SeedSystem);
        var captured = new List<LogRecord>();
        ITraceLogger providersLogger = new StreamingTraceLogger(new RecordingProgress(captured.Add), LogLevel.Trace)
            .ForCategory(LogCategories.OfflineProviders);
        using OfflineImageProviderExtractor extractor = OfflineImageProviderExtractor.TryCreate(image.ImageRoot, providersLogger)!;

        extractor.ReadImageProvenance();

        Assert.NotEmpty(captured);
        Assert.All(captured, record => Assert.Equal(LogCategories.OfflineProviders, record.Category));
    }

    [Fact]
    public void ReadImageProvenance_WhenCurrentVersionIsAbsent_ReturnsEmpty()
    {
        using OfflineTestImage image = OfflineTestImage.Create(seedSoftware: _ => { }, SeedSystem);
        using OfflineImageProviderExtractor extractor = OfflineImageProviderExtractor.TryCreate(image.ImageRoot, logger: null)!;

        Assert.Equal(SourceOsProvenance.Empty, extractor.ReadImageProvenance());
    }

    [Fact]
    public void TryBuildModernProvider_WhenResourceIsNotAValidWevtModule_ReturnsNullGracefully()
    {
        using OfflineTestImage image = OfflineTestImage.Create(SeedSoftware, SeedSystem);
        using OfflineImageProviderExtractor extractor = OfflineImageProviderExtractor.TryCreate(image.ImageRoot, logger: null)!;

        OfflinePublisherRegistration registration = Assert.Single(extractor.ReadModernRegistrations());

        Assert.Null(extractor.TryBuildModernProvider(registration));
    }

    [Fact]
    public void TryCreate_LoadsHivesAndExposesCatalogAndLegacyEnumeration()
    {
        using OfflineTestImage image = OfflineTestImage.Create(SeedSoftware, SeedSystem);

        using OfflineImageProviderExtractor? extractor = OfflineImageProviderExtractor.TryCreate(image.ImageRoot, logger: null);

        Assert.NotNull(extractor);

        OfflinePublisherRegistration registration = Assert.Single(extractor!.ReadModernRegistrations());
        Assert.Equal("Modern-Test-Provider", registration.ProviderName);
        Assert.Equal(
            Path.Combine(image.RootDirectory, "Windows", "System32", "modern.dll"),
            registration.ResourceFilePath,
            ignoreCase: true);

        Assert.Contains("LegacyTestProvider", extractor.EnumerateLegacyProviderNames());
    }

    private static void SeedSoftware(RegistryKey software)
    {
        using (RegistryKey currentVersion = software.CreateSubKey(@"Microsoft\Windows NT\CurrentVersion"))
        {
            currentVersion.SetValue("CurrentBuildNumber", "20348", RegistryValueKind.String);
            currentVersion.SetValue("UBR", 2700, RegistryValueKind.DWord);
            currentVersion.SetValue("EditionID", "ServerDatacenter", RegistryValueKind.String);
            currentVersion.SetValue("DisplayVersion", "21H2", RegistryValueKind.String);
        }

        using RegistryKey publisher = software.CreateSubKey($@"{PublishersKeyPath}\{TestGuid}");
        publisher.SetValue(null, "Modern-Test-Provider");
        publisher.SetValue("ResourceFileName", @"%SystemRoot%\System32\modern.dll", RegistryValueKind.ExpandString);
    }

    private static void SeedSystem(RegistryKey system)
    {
        using (RegistryKey select = system.CreateSubKey("Select")) { select.SetValue("Current", 1, RegistryValueKind.DWord); }

        using RegistryKey provider = system.CreateSubKey(@"ControlSet001\Services\EventLog\Application\LegacyTestProvider");
        provider.SetValue("EventMessageFile", @"C:\Windows\System32\legacy.dll", RegistryValueKind.ExpandString);
    }

    private sealed class RecordingProgress(Action<LogRecord> onReport) : IProgress<LogRecord>
    {
        public void Report(LogRecord value) => onReport(value);
    }
}
