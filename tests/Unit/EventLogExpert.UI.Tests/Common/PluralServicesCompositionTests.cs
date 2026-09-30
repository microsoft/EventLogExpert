// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization.Plural;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Abstractions.Handlers;
using Microsoft.Extensions.Logging;

namespace EventLogExpert.UI.Tests.Common;

[Collection("PluralServices")]
public sealed class PluralServicesCompositionTests : IDisposable
{
    public PluralServicesCompositionTests() => PluralServices.Reset();

    [Fact]
    public void ConfigureFromLogger_InstallsSharedResourceNeutralPatternProvider()
    {
        PluralServicesComposition.ConfigureFromLogger(new RecordingTraceLogger());

        Assert.IsType<SharedResourceNeutralPatternProvider>(PluralServices.Current.NeutralPatternProvider);
    }

    [Fact]
    public void ConfigureFromLogger_NullLogger_Throws() =>
        Assert.Throws<ArgumentNullException>(() => PluralServicesComposition.ConfigureFromLogger(null!));

    [Fact]
    public void ConfigureFromLogger_RoutesMalformedPatternReportToLoggerAtWarning()
    {
        var logger = new RecordingTraceLogger();

        PluralServicesComposition.ConfigureFromLogger(logger);
        PluralServices.Current.Diagnostics.ReportMalformedPattern(
            "Updates_Available", "fr-FR", "{bad", "unexpected token '{'");

        string warning = Assert.Single(logger.WarnMessages);

        // Assert on the distinguishing pieces the caller supplied rather than the exact sentence, so the message can be
        // reworded without breaking the test as long as it still carries the key, culture, and error.
        Assert.Contains("Updates_Available", warning);
        Assert.Contains("fr-FR", warning);
        Assert.Contains("unexpected token '{'", warning);
    }

    public void Dispose() => PluralServices.Reset();

    [Fact]
    public void TryConfigure_WhenAlreadyConfigured_KeepsFirstConfigurationAndReturnsFalse()
    {
        var firstDiagnostics = new RecordingDiagnostics();
        var firstProvider = new StubNeutralProvider();
        PluralServices.TryConfigure(firstDiagnostics, firstProvider);
        int generationAfterFirst = PluralServices.Current.Generation;

        bool installedSecond = PluralServices.TryConfigure(new RecordingDiagnostics(), new StubNeutralProvider());

        Assert.False(installedSecond);

        // First-wins must hold for BOTH services and the generation: a no-op second call that swapped the neutral
        // provider or bumped the generation would silently reset report-once dedup, so pin all three.
        Assert.Same(firstDiagnostics, PluralServices.Current.Diagnostics);
        Assert.Same(firstProvider, PluralServices.Current.NeutralPatternProvider);
        Assert.Equal(generationAfterFirst, PluralServices.Current.Generation);
    }

    [Fact]
    public void TryConfigure_WhenNotYetConfigured_InstallsServicesAndReturnsTrue()
    {
        var diagnostics = new RecordingDiagnostics();
        var neutralProvider = new StubNeutralProvider();

        bool installed = PluralServices.TryConfigure(diagnostics, neutralProvider);

        Assert.True(installed);
        Assert.Same(diagnostics, PluralServices.Current.Diagnostics);
        Assert.Same(neutralProvider, PluralServices.Current.NeutralPatternProvider);
    }

    private sealed class RecordingDiagnostics : IPluralDiagnostics
    {
        public void ReportMalformedPattern(string key, string cultureName, string pattern, string error) { }
    }

    private sealed class RecordingTraceLogger : ITraceLogger
    {
        public LogLevel MinimumLevel => LogLevel.Trace;

        public List<string> WarnMessages { get; } = [];

        public void Critical(CriticalLogHandler handler) => handler.ToStringAndClear();

        public void Debug(DebugLogHandler handler) => handler.ToStringAndClear();

        public void Error(ErrorLogHandler handler) => handler.ToStringAndClear();

        public void Information(InformationLogHandler handler) => handler.ToStringAndClear();

        public void Trace(TraceLogHandler handler) => handler.ToStringAndClear();

        public void Warning(WarningLogHandler handler) => WarnMessages.Add(handler.ToStringAndClear());
    }

    private sealed class StubNeutralProvider : INeutralPatternProvider
    {
        public string? GetNeutralPattern(string key) => null;
    }
}
