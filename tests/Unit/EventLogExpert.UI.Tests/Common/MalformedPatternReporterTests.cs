// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization.Plural;

namespace EventLogExpert.UI.Tests.Common;

[Collection("PluralServices")]
public sealed class MalformedPatternReporterTests : IDisposable
{
    public MalformedPatternReporterTests() => ResetServices();

    public void Dispose() => ResetServices();

    [Fact]
    public void ReportOnce_AfterReconfiguration_ReportsAgainToTheNewSink()
    {
        var first = new RecordingDiagnostics();
        PluralServices.Configure(first, new StubNeutralProvider(_ => null));
        MalformedPatternReporter.ReportOnce(PluralServices.Current, "K", "en", "p", "e");
        Assert.Single(first.Reports);

        PluralServices.Reset();
        var second = new RecordingDiagnostics();
        PluralServices.Configure(second, new StubNeutralProvider(_ => null));
        MalformedPatternReporter.ReportOnce(PluralServices.Current, "K", "en", "p", "e");

        // A new configuration generation is a fresh dedup namespace, so the same key/culture notifies the new sink.
        Assert.Single(second.Reports);
    }

    [Fact]
    public void ReportOnce_DifferentCulturesForSameKey_ReportsPerCulture()
    {
        var diagnostics = new RecordingDiagnostics();
        PluralServices.Configure(diagnostics, new StubNeutralProvider(_ => null));
        PluralServices.Snapshot services = PluralServices.Current;

        MalformedPatternReporter.ReportOnce(services, "K", "en", "p", "e");
        MalformedPatternReporter.ReportOnce(services, "K", "fr", "p", "e");

        Assert.Equal(2, diagnostics.Reports.Count);
    }

    [Fact]
    public void ReportOnce_SameKeyAndCulture_ReportsExactlyOnce()
    {
        var diagnostics = new RecordingDiagnostics();
        PluralServices.Configure(diagnostics, new StubNeutralProvider(_ => null));
        PluralServices.Snapshot services = PluralServices.Current;

        MalformedPatternReporter.ReportOnce(services, "K", "en-US", "pattern", "error");
        MalformedPatternReporter.ReportOnce(services, "K", "en-US", "pattern", "error");

        (string Key, string Culture, string Pattern, string Error) report = Assert.Single(diagnostics.Reports);
        Assert.Equal("K", report.Key);
        Assert.Equal("en-US", report.Culture);
        Assert.Equal("pattern", report.Pattern);
        Assert.Equal("error", report.Error);
    }

    [Fact]
    public void ReportOnce_WithThrowingSink_DoesNotThrow()
    {
        PluralServices.Configure(new RecordingDiagnostics { Throw = true }, new StubNeutralProvider(_ => null));

        Exception? exception = Record.Exception(
            () => MalformedPatternReporter.ReportOnce(PluralServices.Current, "K", "en", "p", "e"));

        Assert.Null(exception);
    }

    private static void ResetServices()
    {
        PluralServices.Reset();
        MalformedPatternReporter.Reset();
    }

    private sealed class RecordingDiagnostics : IPluralDiagnostics
    {
        public List<(string Key, string Culture, string Pattern, string Error)> Reports { get; } = [];

        public bool Throw { get; init; }

        public void ReportMalformedPattern(string key, string cultureName, string pattern, string error)
        {
            if (Throw)
            {
                throw new InvalidOperationException("sink boom");
            }

            Reports.Add((key, cultureName, pattern, error));
        }
    }

    private sealed class StubNeutralProvider(Func<string, string?> resolve) : INeutralPatternProvider
    {
        public string? GetNeutralPattern(string key) => resolve(key);
    }
}
