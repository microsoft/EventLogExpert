// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.Eventing.IntegrationTests.Resolvers;

// Guards the fine Resolution.* category wiring. ITraceLogger.ForCategory has a no-op DEFAULT (returns this), so a
// missing or mistyped ForCategory at a construction site silently collapses the sub-concern back to the parent
// Resolution category and still compiles - the message-based resolver tests never observe it. The recording logger
// below overrides ForCategory to capture the exact category requested at each site, so a dropped or wrong call fails
// these tests instead of shipping unnoticed.
public sealed class EventResolverCategoryStampTests
{
    [Fact]
    public void Constructor_DerivesFineCategoriesForTheSubResolvers()
    {
        var logger = new CategoryRecordingLogger();

        using var resolver = new EventResolver(logger: logger);

        Assert.Contains(LogCategories.ResolutionModern, logger.Categories);
        Assert.Contains(LogCategories.ResolutionTasks, logger.Categories);
        Assert.Contains(LogCategories.ResolutionDescription, logger.Categories);
    }

    [Fact]
    public void LoadProviderDetails_LocalFallback_CategorizesTheProviderLookupAsResolutionProviders()
    {
        var logger = new CategoryRecordingLogger();
        using var resolver = new EventResolver(logger: logger);

        // No metadata paths and no databases, so resolution falls straight through to ResolveFromLocalProvider
        // (EventResolver.cs:244) - exactly one provider-lookup construction, stamped Resolution.Providers.
        resolver.LoadProviderDetails(new EventRecord { ProviderName = "ELX.Test.MissingProvider.Local", Id = 1000 });

        Assert.Equal(1, logger.Categories.Count(category => category == LogCategories.ResolutionProviders));
    }

    [Fact]
    public void LoadProviderDetails_ThroughTheMtaPath_CategorizesTheMtaProviderLookupAsResolutionProviders()
    {
        var logger = new CategoryRecordingLogger();
        using var resolver = new EventResolver(logger: logger);

        // A non-empty metadata path routes resolution through TryResolveFromMta first (the primary source for
        // exported logs, and the site originally missed). The bogus path yields nothing, so resolution then falls
        // through to the local provider - so TWO provider lookups are constructed: the MTA site (EventResolver.cs:314)
        // AND the local site (244). The local-only test above proves the local site contributes exactly one, so the
        // second stamp here can only come from the MTA site. A dropped ForCategory at 314 would make this 1, not 2.
        resolver.SetMetadataPaths([Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mui")]);

        resolver.LoadProviderDetails(new EventRecord { ProviderName = "ELX.Test.MissingProvider.Mta", Id = 1000 });

        Assert.Equal(2, logger.Categories.Count(category => category == LogCategories.ResolutionProviders));
    }

    private sealed class CategoryRecordingLogger : ITraceLogger
    {
        public List<string> Categories { get; } = [];

        public LogLevel MinimumLevel => LogLevel.Trace;

        public void Critical(CriticalLogHandler handler) => handler.ToStringAndClear();

        public void Debug(DebugLogHandler handler) => handler.ToStringAndClear();

        public void Error(ErrorLogHandler handler) => handler.ToStringAndClear();

        public ITraceLogger ForCategory(string category)
        {
            Categories.Add(category);

            return this;
        }

        public void Information(InformationLogHandler handler) => handler.ToStringAndClear();

        public void Trace(TraceLogHandler handler) => handler.ToStringAndClear();

        public void Warning(WarningLogHandler handler) => handler.ToStringAndClear();
    }
}
