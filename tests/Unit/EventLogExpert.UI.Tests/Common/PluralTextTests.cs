// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Localization.Plural;
using EventLogExpert.UI.Common;
using Microsoft.Extensions.Localization;
using System.Globalization;

namespace EventLogExpert.UI.Tests.Common;

[CollectionDefinition("PluralServices", DisableParallelization = true)]
public sealed class PluralServicesCollection;

[Collection("PluralServices")]
public sealed class PluralTextTests : IDisposable
{
    public PluralTextTests() => ResetServices();

    [Fact]
    public void Configure_CalledTwice_Throws()
    {
        PluralServices.Configure(new RecordingDiagnostics(), new StubNeutralProvider(_ => null));

        Assert.Throws<InvalidOperationException>(
            () => PluralServices.Configure(new RecordingDiagnostics(), new StubNeutralProvider(_ => null)));
    }

    public void Dispose() => ResetServices();

    [Theory]
    [InlineData(1, "1 file")]
    [InlineData(2, "2 files")]
    public void Format_SelectsPluralCategoryFromResolvedPattern(int count, string expected)
    {
        var localizer = new StubLocalizer(_ => "{count, plural, one {{count} file} other {{count} files}}");

        Assert.Equal(expected, PluralText.Format(localizer, "AnyKey", ("count", count)));
    }

    [Fact]
    public void Format_UnderUnsupportedUiCulture_ResolvesToEnglishRules()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
            var localizer = new StubLocalizer(_ => "{count, plural, one {{count} file} other {{count} files}}");

            // Russian 'one' includes 21, but the resolved content culture is en, so 21 selects 'other'.
            Assert.Equal("21 files", PluralText.Format(localizer, "AnyKey", ("count", 21)));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Format_WithMalformedLocalizedPattern_RendersNeutralEnglishPattern()
    {
        PluralServices.Configure(
            new RecordingDiagnostics(),
            new StubNeutralProvider(_ => "{count, plural, one {{count} neutral} other {{count} neutrals}}"));
        var localizer = new StubLocalizer(_ => "{count, plural, one {malformed}}");

        Assert.Equal("1 neutral", PluralText.Format(localizer, "AnyKey", ("count", 1)));
    }

    [Fact]
    public void Format_WithMalformedPatternAndNoNeutralFallback_ReturnsKey()
    {
        // Missing 'other' -> IcuMessageException; the neutral provider has no such key -> the key is the safe last resort (never raw ICU).
        var localizer = new StubLocalizer(_ => "{count, plural, one {only one}}");

        Assert.Equal("NonexistentPluralKey", PluralText.Format(localizer, "NonexistentPluralKey", ("count", 1)));
    }

    [Fact]
    public void Format_WithMalformedPattern_ReportsOncePerKey()
    {
        var diagnostics = new RecordingDiagnostics();
        PluralServices.Configure(diagnostics, new StubNeutralProvider(_ => null));
        var localizer = new StubLocalizer(_ => "{count, plural, one {only one}}");

        PluralText.Format(localizer, "MalformedKey", ("count", 1));
        PluralText.Format(localizer, "MalformedKey", ("count", 2));

        Assert.Single(diagnostics.Reports);
        Assert.Equal("MalformedKey", diagnostics.Reports[0].Key);
    }

    [Fact]
    public void Format_WithMarkerLocalizerStylePattern_ReturnsVerbatim()
    {
        var localizer = new StubLocalizer(name => $"[[{name}]]");

        Assert.Equal("[[SomeKey]]", PluralText.Format(localizer, "SomeKey", ("count", 3)));
    }

    [Fact]
    public void Format_WithThrowingServices_DoesNotThrowAndFallsToKey()
    {
        PluralServices.Configure(
            new RecordingDiagnostics { Throw = true },
            new StubNeutralProvider(_ => throw new InvalidOperationException("provider boom")));
        var localizer = new StubLocalizer(_ => "{count, plural, one {only one}}");

        Assert.Equal("Key", PluralText.Format(localizer, "Key", ("count", 1)));
    }

    private static void ResetServices()
    {
        PluralServices.Reset();
        PluralText.Reset();
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

    private sealed class StubLocalizer(Func<string, string> resolve) : IStringLocalizer<SharedResource>
    {
        public LocalizedString this[string name] => new(name, resolve(name));

        public LocalizedString this[string name, params object[] arguments] => new(name, resolve(name));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private sealed class StubNeutralProvider(Func<string, string?> resolve) : INeutralPatternProvider
    {
        public string? GetNeutralPattern(string key) => resolve(key);
    }
}
