// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization.Plural;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.Localization;
using System.Globalization;

namespace EventLogExpert.UI.Tests.Localization;

[Collection("PluralServices")]
public sealed class LocalizableTextResolverPluralTests : IDisposable
{
    private const string ProviderPattern =
        "{count, plural, one {Found {count} provider in {0}.} other {Found {count} providers in {0}.}}";

    public LocalizableTextResolverPluralTests() => ResetServices();

    public void Dispose() => ResetServices();

    [Fact]
    public void Resolve_WithMalformedPatternAndNeutralProvider_RendersNeutralEnglishAndReports()
    {
        var diagnostics = new RecordingDiagnostics();
        PluralServices.Configure(
            diagnostics,
            new StubNeutralProvider(_ => "{count, plural, one {neutral {count}} other {neutral {count}}}"));
        var localizer = new StubLocalizer(_ => "{count, plural, one {malformed}}");
        var text = new LocalizableText("AnyKey", [], PluralCount: 2);

        Assert.Equal("neutral 2", LocalizableTextResolver.Resolve(localizer, text));
        Assert.Single(diagnostics.Reports);
    }

    [Fact]
    public void Resolve_WithMalformedPatternAndNoNeutralPattern_FallsBackToKey()
    {
        var localizer = new StubLocalizer(_ => "{count, plural, one {malformed}}");
        var text = new LocalizableText("AnyKey", [], PluralCount: 1);

        Assert.Equal("AnyKey", LocalizableTextResolver.Resolve(localizer, text));
    }

    [Fact]
    public void Resolve_WithMalformedPattern_ReportsOncePerKeyUsingTheResourceFetchCulture()
    {
        var diagnostics = new RecordingDiagnostics();
        PluralServices.Configure(diagnostics, new StubNeutralProvider(_ => null));
        var localizer = new StubLocalizer(_ => "{count, plural, one {malformed}}");
        var text = new LocalizableText("AnyKey", [], PluralCount: 1);

        LocalizableTextResolver.Resolve(localizer, text);
        LocalizableTextResolver.Resolve(localizer, text);

        (string Key, string Culture) report = Assert.Single(diagnostics.Reports);
        Assert.Equal("AnyKey", report.Key);
        Assert.Equal(CultureInfo.CurrentUICulture.Name, report.Culture);
    }

    [Theory]
    [InlineData(1, "Found 1 provider in providers.db.")]
    [InlineData(2, "Found 2 providers in providers.db.")]
    public void Resolve_WithPluralCount_RoutesThroughIcuFormatter(int count, string expected)
    {
        var localizer = new StubLocalizer(_ => ProviderPattern);
        var text = new LocalizableText("AnyKey", ["providers.db"], PluralCount: count);

        Assert.Equal(expected, LocalizableTextResolver.Resolve(localizer, text));
    }

    [Fact]
    public void Resolve_WithoutPluralCount_UsesStringFormatPath()
    {
        var localizer = new StubLocalizer(_ => "{0} widgets");

        Assert.Equal("3 widgets", LocalizableTextResolver.Resolve(localizer, "AnyKey", ["3"], "AnyKey"));
    }

    private static void ResetServices()
    {
        PluralServices.Reset();
        MalformedPatternReporter.Reset();
    }

    private sealed class RecordingDiagnostics : IPluralDiagnostics
    {
        public List<(string Key, string Culture)> Reports { get; } = [];

        public void ReportMalformedPattern(string key, string cultureName, string pattern, string error) =>
            Reports.Add((key, cultureName));
    }

    private sealed class StubLocalizer(Func<string, string> resolve) : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, resolve(name));

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(CultureInfo.InvariantCulture, resolve(name), arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private sealed class StubNeutralProvider(Func<string, string?> resolve) : INeutralPatternProvider
    {
        public string? GetNeutralPattern(string key) => resolve(key);
    }
}
