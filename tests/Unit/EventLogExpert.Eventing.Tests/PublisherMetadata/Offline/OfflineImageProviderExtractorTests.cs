// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Offline;
using Microsoft.Win32;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata.Offline;

public sealed class OfflineImageProviderExtractorTests
{
    private const string PublishersKeyPath = @"Microsoft\Windows\CurrentVersion\WINEVT\Publishers";
    private const string TestGuid = "{33333333-3333-3333-3333-333333333333}";

    [Fact]
    public void TryBuildModernProvider_WhenResourceIsNotAValidWevtModule_ReturnsNullGracefully()
    {
        using OfflineTestImage image = OfflineTestImage.Create(SeedSoftware, SeedSystem);
        using OfflineImageProviderExtractor extractor = OfflineImageProviderExtractor.TryCreate(image.ImageRoot, logger: null)!;

        OfflinePublisherRegistration registration = Assert.Single(extractor.ReadModernRegistrations());

        // The re-rooted resource path does not exist in the synthetic image, so the WEVT parse fails closed (no crash).
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
}
