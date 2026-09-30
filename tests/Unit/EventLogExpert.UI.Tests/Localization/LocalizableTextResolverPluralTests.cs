// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization.Plural;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.Localization;
using System.Globalization;
using System.Text.RegularExpressions;

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

        string resolved = LocalizableTextResolver.Resolve(localizer, text);

        Assert.Contains("2", resolved, StringComparison.Ordinal);
        Assert.DoesNotContain("AnyKey", resolved, StringComparison.Ordinal);
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

    [Fact]
    public void Resolve_WithPluralCount_RoutesThroughIcuFormatter()
    {
        var localizer = new StubLocalizer(_ => ProviderPattern);
        var singularText = new LocalizableText("AnyKey", ["providers.db"], PluralCount: 1);
        var pluralText = new LocalizableText("AnyKey", ["providers.db"], PluralCount: 2);

        string singular = LocalizableTextResolver.Resolve(localizer, singularText);
        string plural = LocalizableTextResolver.Resolve(localizer, pluralText);

        Assert.Contains("1", singular, StringComparison.Ordinal);
        Assert.Contains("2", plural, StringComparison.Ordinal);
        Assert.Contains("providers.db", singular, StringComparison.Ordinal);
        Assert.Contains("providers.db", plural, StringComparison.Ordinal);
        Assert.NotEqual(NormalizeFormattedNumbers(singular), NormalizeFormattedNumbers(plural));
    }

    [Fact]
    public void Resolve_WithoutPluralCount_UsesStringFormatPath()
    {
        var localizer = new StubLocalizer(_ => "{0} widgets");

        string resolved = LocalizableTextResolver.Resolve(localizer, "AnyKey", ["3"], "AnyKey");

        Assert.Contains("3", resolved, StringComparison.Ordinal);
        Assert.DoesNotContain("{0}", resolved, StringComparison.Ordinal);
    }

    private static string NormalizeFormattedNumbers(string value) =>
        Regex.Replace(value, @"\d+(?:,\d{3})*(?:\.\d+)?", "#");

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
