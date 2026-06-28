// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata;
using EventLogExpert.Eventing.PublisherMetadata.Offline;
using EventLogExpert.Eventing.Tests.PublisherMetadata.Offline;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using EventLogExpert.Provider.Resolution;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata;

public sealed class OfflineImageProviderSourceTests
{
    private static readonly SourceOsProvenance s_testProvenance = new(22621, 3, "ServerStandard", "23H2");

    [Fact]
    public void Enumerate_DuplicateModernRegistrationsForOneName_YieldOnce()
    {
        var extractor = new FakeExtractor
        {
            ModernRegistrations = { Registration("Dup"), Registration("Dup") },
            BuildModern = registration => NonEmpty(registration.ProviderName)
        };

        List<ProviderDetails> result = Enumerate(extractor);

        Assert.Equal(["Dup"], result.Select(details => details.ProviderName));
    }

    [Fact]
    public void Enumerate_ExcludeSet_AppliesToBothModernAndLegacy()
    {
        var extractor = new FakeExtractor
        {
            ModernRegistrations = { Registration("Keep"), Registration("Excluded") },
            LegacyProviderNames = { "Excluded", "Other" },
            BuildModern = registration => NonEmpty(registration.ProviderName),
            BuildLegacy = NonEmpty
        };

        var exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Excluded" };

        List<ProviderDetails> result = Enumerate(extractor, excludeProviderNames: exclude);

        Assert.Equal(["Keep", "Other"], result.Select(details => details.ProviderName));
    }

    [Fact]
    public void Enumerate_FailedModernBuild_DoesNotSuppressSameNameLegacyProvider()
    {
        var extractor = new FakeExtractor
        {
            ModernRegistrations = { Registration("X") },
            LegacyProviderNames = { "X" },
            BuildModern = _ => null,
            BuildLegacy = NonEmpty
        };

        List<ProviderDetails> result = Enumerate(extractor);

        // A name is marked seen only after a non-empty yield, so a modern build that fails closed leaves the legacy
        // provider of the same name free to be built and yielded.
        Assert.Equal(["X"], result.Select(details => details.ProviderName));
    }

    [Fact]
    public void Enumerate_LegacyNameMatchingModern_YieldsModernAndNeverBuildsTheLegacyDuplicate()
    {
        var extractor = new FakeExtractor
        {
            ModernRegistrations = { Registration("Shared") },
            LegacyProviderNames = { "Shared", "Legacy-Only" },
            BuildModern = registration => NonEmpty(registration.ProviderName),
            BuildLegacy = NonEmpty
        };

        List<ProviderDetails> result = Enumerate(extractor);

        // The modern build already populates the legacy tables, so the same-named legacy registration is skipped
        // before it is even built.
        Assert.Equal(["Shared", "Legacy-Only"], result.Select(details => details.ProviderName));
        Assert.DoesNotContain("Shared", extractor.LegacyBuildRequests);
        Assert.Contains("Legacy-Only", extractor.LegacyBuildRequests);
    }

    [Fact]
    public void Enumerate_NullOrEmptyModernBuilds_AreSkipped()
    {
        var extractor = new FakeExtractor
        {
            ModernRegistrations = { Registration("Null-Build"), Registration("Empty-Build") },
            BuildModern = registration =>
                registration.ProviderName == "Null-Build" ? null : Empty(registration.ProviderName)
        };

        Assert.Empty(Enumerate(extractor));
    }

    [Fact]
    public void Enumerate_RegexFilter_AppliesToBothModernAndLegacyBeforeBuilding()
    {
        var extractor = new FakeExtractor
        {
            ModernRegistrations = { Registration("Keep-A"), Registration("Drop-B") },
            LegacyProviderNames = { "Keep-C", "Drop-D" },
            BuildModern = registration => NonEmpty(registration.ProviderName),
            BuildLegacy = NonEmpty
        };

        List<ProviderDetails> result = Enumerate(extractor, regex: new Regex("^Keep-"));

        Assert.Equal(["Keep-A", "Keep-C"], result.Select(details => details.ProviderName));
        Assert.DoesNotContain(extractor.ModernBuildRequests, registration => registration.ProviderName == "Drop-B");
        Assert.DoesNotContain("Drop-D", extractor.LegacyBuildRequests);
    }

    [Fact]
    public void Enumerate_YieldsModernThenLegacy_StampedWithImageProvenance()
    {
        var extractor = new FakeExtractor
        {
            Provenance = s_testProvenance,
            ModernRegistrations = { Registration("Modern-A") },
            LegacyProviderNames = { "Legacy-B" },
            BuildModern = registration => NonEmpty(registration.ProviderName),
            BuildLegacy = NonEmpty
        };

        List<ProviderDetails> result = Enumerate(extractor);

        // Modern providers are enumerated before pure-legacy providers.
        Assert.Equal(["Modern-A", "Legacy-B"], result.Select(details => details.ProviderName));
        Assert.All(result, details =>
        {
            Assert.Equal(s_testProvenance.Build, details.SourceOsBuild);
            Assert.Equal(s_testProvenance.Revision, details.SourceOsRevision);
            Assert.Equal(s_testProvenance.Edition, details.SourceOsEdition);
            Assert.Equal(s_testProvenance.DisplayVersion, details.SourceOsDisplayVersion);
        });
    }

    [Fact]
    public void LoadProviders_PathIsNotAWindowsImage_LogsErrorAndYieldsEmpty()
    {
        var logger = new CapturingTraceLogger();
        string emptyDirectory = Path.Combine(Path.GetTempPath(), "elx_not_image_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyDirectory);

        try
        {
            List<ProviderDetails> result =
                OfflineImageProviderSource.LoadProviders(emptyDirectory, logger).ToList();

            Assert.Empty(result);
            Assert.Contains(
                logger.Errors,
                message => message.Contains("not a readable Windows image", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(emptyDirectory, recursive: true);
        }
    }

    [Fact]
    public void LoadProviders_SyntheticImage_RunsTheFullPipelineWithoutCrashing()
    {
        var logger = new CapturingTraceLogger();
        using OfflineTestImage image = OfflineTestImage.Create(SeedSoftware, SeedSystem);

        // A real extractor over a synthetic image: the re-rooted DLLs do not exist, so every modern and legacy build
        // fails closed and the enumeration yields nothing - but TryCreate, both loops, the provenance read, and the
        // hive unload all run end to end.
        List<ProviderDetails> result =
            OfflineImageProviderSource.LoadProviders(image.RootDirectory, logger).ToList();

        Assert.Empty(result);
    }

    private static ProviderDetails Empty(string providerName) => new() { ProviderName = providerName };

    private static List<ProviderDetails> Enumerate(
        FakeExtractor extractor,
        Regex? regex = null,
        IReadOnlySet<string>? excludeProviderNames = null) =>
        OfflineImageProviderSource.Enumerate(extractor, regex, excludeProviderNames).ToList();

    private static ProviderDetails NonEmpty(string providerName) =>
        new() { ProviderName = providerName, Keywords = new Dictionary<long, string> { [1] = "keyword" } };

    private static OfflinePublisherRegistration Registration(string providerName) =>
        new(Guid.NewGuid(), providerName, ResourceFilePath: null, MessageFilePaths: [], ParameterFilePath: null);

    private static void SeedSoftware(RegistryKey software)
    {
        using RegistryKey publisher = software.CreateSubKey(
            @"Microsoft\Windows\CurrentVersion\WINEVT\Publishers\{44444444-4444-4444-4444-444444444444}");
        publisher.SetValue(null, "Modern-Image-Provider");
        publisher.SetValue("ResourceFileName", @"%SystemRoot%\System32\absent.dll", RegistryValueKind.ExpandString);
    }

    private static void SeedSystem(RegistryKey system)
    {
        using (RegistryKey select = system.CreateSubKey("Select"))
        {
            select.SetValue("Current", 1, RegistryValueKind.DWord);
        }

        using RegistryKey provider =
            system.CreateSubKey(@"ControlSet001\Services\EventLog\Application\LegacyImageProvider");
        provider.SetValue("EventMessageFile", @"C:\Windows\System32\absent.dll", RegistryValueKind.ExpandString);
    }

    private sealed class CapturingTraceLogger : ITraceLogger
    {
        public List<string> Errors { get; } = [];

        public LogLevel MinimumLevel => LogLevel.Trace;

        public void Critical(CriticalLogHandler handler) => handler.ToStringAndClear();

        public void Debug(DebugLogHandler handler) => handler.ToStringAndClear();

        public void Error(ErrorLogHandler handler) => Errors.Add(handler.ToStringAndClear());

        public void Information(InformationLogHandler handler) => handler.ToStringAndClear();

        public void Trace(TraceLogHandler handler) => handler.ToStringAndClear();

        public void Warning(WarningLogHandler handler) => handler.ToStringAndClear();
    }

    private sealed class FakeExtractor : IOfflineImageProviderExtractor
    {
        public Func<string, ProviderDetails?> BuildLegacy { get; init; } = _ => null;

        public Func<OfflinePublisherRegistration, ProviderDetails?> BuildModern { get; init; } = _ => null;

        public bool Disposed { get; private set; }

        public List<string> LegacyBuildRequests { get; } = [];

        public List<string> LegacyProviderNames { get; init; } = [];

        public List<OfflinePublisherRegistration> ModernBuildRequests { get; } = [];

        public List<OfflinePublisherRegistration> ModernRegistrations { get; init; } = [];

        public SourceOsProvenance Provenance { get; init; } = SourceOsProvenance.Empty;

        public void Dispose() => Disposed = true;

        public IReadOnlyList<string> EnumerateLegacyProviderNames() => LegacyProviderNames;

        public SourceOsProvenance ReadImageProvenance() => Provenance;

        public IReadOnlyList<OfflinePublisherRegistration> ReadModernRegistrations() => ModernRegistrations;

        public ProviderDetails? TryBuildLegacyProvider(string providerName)
        {
            LegacyBuildRequests.Add(providerName);

            return BuildLegacy(providerName);
        }

        public ProviderDetails? TryBuildModernProvider(OfflinePublisherRegistration registration)
        {
            ModernBuildRequests.Add(registration);

            return BuildModern(registration);
        }
    }
}
