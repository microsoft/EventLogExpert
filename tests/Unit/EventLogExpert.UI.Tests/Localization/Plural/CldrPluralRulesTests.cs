// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization.Plural;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EventLogExpert.UI.Tests.Localization.Plural;

public sealed partial class CldrPluralRulesTests
{
    [Fact]
    public void EmbeddedResourceName_UsesExpectedLogicalSuffix() =>
        Assert.EndsWith("EventLogExpert.Localization.Plural.cldr-plurals-cardinal.json", CldrPluralRules.GetEmbeddedResourceName(), StringComparison.Ordinal);

    [Fact]
    public void EmbeddedResource_MatchesRecordedCldrChecksum()
    {
        const string expectedHash = "6c0a48e9bcfc25856f90202f703c2c7f89c105d6868f7712a943a6ed2dcbe8f4";

        using Stream stream = typeof(CldrPluralRules).Assembly.GetManifestResourceStream(CldrPluralRules.GetEmbeddedResourceName())!;
        byte[] hash = SHA256.HashData(stream);

        Assert.Equal(expectedHash, Convert.ToHexStringLower(hash));
    }

    [Fact]
    public void FromComponents_ForCompactSamples_UsesMantissaOperandsAndExponentAliases()
    {
        PluralOperands operands = PluralOperands.FromComponents(1.1m, 6);

        Assert.Equal(1.1m, operands.N);
        Assert.Equal((UInt128)1, operands.I);
        Assert.Equal(1, operands.V);
        Assert.Equal(1, operands.W);
        Assert.Equal((UInt128)1, operands.F);
        Assert.Equal((UInt128)1, operands.T);
        Assert.Equal(6, operands.C);
        Assert.Equal(6, operands.E);
    }

    [Fact]
    public void FromDecimal_WithVisibleTrailingZeros_PreservesVisibleFractionOperands()
    {
        PluralOperands operands = PluralOperands.FromDecimal(1.10m);

        Assert.Equal(1.10m, operands.N);
        Assert.Equal((UInt128)1, operands.I);
        Assert.Equal(2, operands.V);
        Assert.Equal(1, operands.W);
        Assert.Equal((UInt128)10, operands.F);
        Assert.Equal((UInt128)1, operands.T);
        Assert.Equal(0, operands.C);
        Assert.Equal(0, operands.E);
    }

    [Fact]
    public void FromDecimal_WithVisibleZeroFraction_DistinguishesOnePointZeroFromIntegerOne()
    {
        Assert.Equal(PluralCategory.One, CldrPluralRules.Select("en", PluralOperands.FromLong(1)));
        Assert.Equal(PluralCategory.Other, CldrPluralRules.Select("en", PluralOperands.FromDecimal(1.0m)));
    }

    [Theory]
    [InlineData("ar", 103, PluralCategory.Few)]
    [InlineData("ar", 111, PluralCategory.Many)]
    [InlineData("ar", 100, PluralCategory.Other)]
    [InlineData("bs", 21, PluralCategory.One)]
    [InlineData("bs", 11, PluralCategory.Other)]
    [InlineData("bs", 22, PluralCategory.Few)]
    [InlineData("bs", 14, PluralCategory.Other)]
    [InlineData("ru", -1, PluralCategory.One)]
    [InlineData("ru", -2, PluralCategory.Few)]
    [InlineData("fr", 0, PluralCategory.One)]
    public void Select_CoversOperatorPrecedenceRangesModuloNegativesAndFallback(string localeName, long value, PluralCategory expectedCategory) =>
        Assert.Equal(expectedCategory, CldrPluralRules.Select(localeName, PluralOperands.FromLong(value)));

    [Fact]
    public void Select_ForEveryEmbeddedSample_ReturnsDeclaredCategory()
    {
        using JsonDocument document = LoadCldrDocument();
        JsonElement pluralRulesElement = document.RootElement
            .GetProperty("supplemental")
            .GetProperty("plurals-type-cardinal");

        foreach (JsonProperty localeProperty in pluralRulesElement.EnumerateObject())
        {
            foreach (JsonProperty ruleProperty in localeProperty.Value.EnumerateObject())
            {
                const string prefix = "pluralRule-count-";
                if (!ruleProperty.Name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                PluralCategory expectedCategory = ParseCategory(ruleProperty.Name[prefix.Length..]);
                foreach ((string sampleToken, PluralOperands operands) in GetSampleOperands(ruleProperty.Value.GetString() ?? string.Empty))
                {
                    PluralCategory actualCategory = CldrPluralRules.Select(localeProperty.Name, operands);
                    Assert.True(
                        expectedCategory == actualCategory,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{localeProperty.Name} {ruleProperty.Name} sample {sampleToken} expected {expectedCategory} actual {actualCategory} operands {operands}"));
                }
            }
        }
    }

    [Fact]
    public void Select_WithLongMinValue_UsesUnsignedMagnitudeWithoutOverflow()
    {
        PluralOperands operands = PluralOperands.FromLong(long.MinValue);

        Assert.Equal(9223372036854775808UL, operands.I);
        Assert.Equal(PluralCategory.Other, CldrPluralRules.Select("en", operands));
    }

    [Fact]
    public void Select_WithSpecificCulture_FallsBackThroughParents()
    {
        PluralCategory category = CldrPluralRules.Select(CultureInfo.GetCultureInfo("en-US"), 1);

        Assert.Equal(PluralCategory.One, category);
    }

    [GeneratedRegex("^([+-]?\\d+(?:\\.\\d+)?)c([+-]?\\d+)$")]
    private static partial Regex CompactSampleRegex();

    private static decimal DecimalPow10(int exponent)
    {
        decimal result = 1m;
        for (int index = 0; index < exponent; index++)
        {
            result *= 10m;
        }

        return result;
    }

    private static IEnumerable<(string SampleToken, PluralOperands Operands)> ExpandSampleToken(string token)
    {
        int rangeSeparatorIndex = token.IndexOf('~', StringComparison.Ordinal);
        if (rangeSeparatorIndex < 0)
        {
            yield return (token, ParseSampleValue(token));
            yield break;
        }

        string startToken = token[..rangeSeparatorIndex];
        string endToken = token[(rangeSeparatorIndex + 1)..];
        int visiblePrecision = Math.Max(GetVisiblePrecision(startToken), GetVisiblePrecision(endToken));
        decimal step = 1m / DecimalPow10(visiblePrecision);
        for (decimal value = decimal.Parse(startToken, NumberStyles.Number, CultureInfo.InvariantCulture);
             value <= decimal.Parse(endToken, NumberStyles.Number, CultureInfo.InvariantCulture);
             value += step)
        {
            yield return (value.ToString(CultureInfo.InvariantCulture), visiblePrecision == 0 ? PluralOperands.FromLong((long)value) : PluralOperands.FromDecimal(value));
        }
    }

    private static IEnumerable<(string SampleToken, PluralOperands Operands)> GetSampleOperands(string ruleText)
    {
        foreach (string sampleSection in GetSampleSections(ruleText))
        {
            foreach (string rawToken in sampleSection.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                string token = rawToken.Trim();
                if (token == "…")
                {
                    continue;
                }

                foreach ((string sampleToken, PluralOperands operands) in ExpandSampleToken(token))
                {
                    yield return (sampleToken, operands);
                }
            }
        }
    }

    private static IEnumerable<string> GetSampleSections(string ruleText)
    {
        MatchCollection markerMatches = SampleMarkerRegex().Matches(ruleText);
        for (int index = 0; index < markerMatches.Count; index++)
        {
            int start = markerMatches[index].Index + markerMatches[index].Length;
            int end = index + 1 < markerMatches.Count ? markerMatches[index + 1].Index : ruleText.Length;
            yield return ruleText[start..end];
        }
    }

    private static int GetVisiblePrecision(string token)
    {
        int decimalIndex = token.IndexOf('.', StringComparison.Ordinal);
        return decimalIndex < 0 ? 0 : token.Length - decimalIndex - 1;
    }

    private static JsonDocument LoadCldrDocument()
    {
        string resourceName = CldrPluralRules.GetEmbeddedResourceName();
        Stream stream = typeof(CldrPluralRules).Assembly.GetManifestResourceStream(resourceName)!;
        return JsonDocument.Parse(stream);
    }

    private static PluralCategory ParseCategory(string categoryName) => categoryName switch
    {
        "zero" => PluralCategory.Zero,
        "one" => PluralCategory.One,
        "two" => PluralCategory.Two,
        "few" => PluralCategory.Few,
        "many" => PluralCategory.Many,
        "other" => PluralCategory.Other,
        _ => throw new InvalidOperationException($"Unknown plural category {categoryName}."),
    };

    private static PluralOperands ParseSampleValue(string token)
    {
        Match compactMatch = CompactSampleRegex().Match(token);
        if (compactMatch.Success)
        {
            decimal mantissa = decimal.Parse(compactMatch.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture);
            int exponent = int.Parse(compactMatch.Groups[2].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            return PluralOperands.FromComponents(mantissa, exponent);
        }

        return token.Contains('.', StringComparison.Ordinal)
            ? PluralOperands.FromDecimal(decimal.Parse(token, NumberStyles.Number, CultureInfo.InvariantCulture))
            : PluralOperands.FromLong(long.Parse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
    }

    [GeneratedRegex("@(integer|decimal)\\s+")]
    private static partial Regex SampleMarkerRegex();
}
