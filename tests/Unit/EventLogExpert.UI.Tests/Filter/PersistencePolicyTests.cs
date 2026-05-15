// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Filter;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EventLogExpert.UI.Tests.Filter;

public sealed class PersistencePolicyTests
{
    [Fact]
    public void BasicFilter_Write_PinsNestedComparisonAndSubFiltersKeys()
    {
        // Arrange — invariant 5 (nested keys): BasicFilter is plain default-STJ (no [JsonConverter]). Its
        // record positional parameters are Comparison and SubFilters, which become the persisted property names.
        // Renaming BasicFilter.Comparison → "Condition" (a plausible F16d rename, since BasicFilterCondition
        // itself is slated to become FilterComparison) would silently change the wire shape and break every
        // Basic-mode filter already persisted. SavedFilter.LoadFromPersisted's re-decompose path can mask the
        // regression on read, so we pin the WRITE-side literal keys here.
        var basicFilter = new BasicFilter(
            new BasicFilterCondition
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = "100"
            },
            []);

        // Act
        string json = JsonSerializer.Serialize(basicFilter);

        // Assert — literal nested keys present; renaming the record properties would fail this test.
        Assert.Contains("\"Comparison\"", json);
        Assert.Contains("\"SubFilters\"", json);
        Assert.DoesNotContain("\"Condition\"", json);
    }

    [Fact]
    public void FilterMode_MemberNames_AreFrozenAcrossF16()
    {
        // Act + Assert — invariant 4: FilterMode is persisted as a string of the CLR member name. Renaming any
        // member would silently fail to deserialize and fall back to FilterMode.Advanced, dropping Basic-mode
        // structure on every persisted row.
        Assert.Equal("Basic", nameof(FilterMode.Basic));
        Assert.Equal("Advanced", nameof(FilterMode.Advanced));
        Assert.Equal("Cached", nameof(FilterMode.Cached));

        var declared = Enum.GetNames<FilterMode>().ToHashSet();
        Assert.Equal(new HashSet<string> { "Basic", "Advanced", "Cached" }, declared);
    }

    [Fact]
    public void HighlightColor_MemberCount_IsFrozenAcrossF16()
    {
        // Act + Assert — pairs with the per-member ordinal theory: if a new color is added or one is removed,
        // the theory above continues to pass but this guard fails, forcing a deliberate update.
        Assert.Equal(28, Enum.GetValues<HighlightColor>().Length);
    }

    [Theory]
    [InlineData(HighlightColor.None, 0)]
    [InlineData(HighlightColor.LightRed, 1)]
    [InlineData(HighlightColor.Red, 2)]
    [InlineData(HighlightColor.DarkRed, 3)]
    [InlineData(HighlightColor.LightOrange, 4)]
    [InlineData(HighlightColor.Orange, 5)]
    [InlineData(HighlightColor.DarkOrange, 6)]
    [InlineData(HighlightColor.LightYellow, 7)]
    [InlineData(HighlightColor.Yellow, 8)]
    [InlineData(HighlightColor.DarkYellow, 9)]
    [InlineData(HighlightColor.LightGreen, 10)]
    [InlineData(HighlightColor.Green, 11)]
    [InlineData(HighlightColor.DarkGreen, 12)]
    [InlineData(HighlightColor.LightTeal, 13)]
    [InlineData(HighlightColor.Teal, 14)]
    [InlineData(HighlightColor.DarkTeal, 15)]
    [InlineData(HighlightColor.LightBlue, 16)]
    [InlineData(HighlightColor.Blue, 17)]
    [InlineData(HighlightColor.DarkBlue, 18)]
    [InlineData(HighlightColor.LightPurple, 19)]
    [InlineData(HighlightColor.Purple, 20)]
    [InlineData(HighlightColor.DarkPurple, 21)]
    [InlineData(HighlightColor.LightMagenta, 22)]
    [InlineData(HighlightColor.Magenta, 23)]
    [InlineData(HighlightColor.DarkMagenta, 24)]
    [InlineData(HighlightColor.LightPink, 25)]
    [InlineData(HighlightColor.Pink, 26)]
    [InlineData(HighlightColor.DarkPink, 27)]
    public void HighlightColor_OrdinalValue_IsFrozenAcrossF16(HighlightColor member, int expectedOrdinal)
    {
        // Act + Assert — invariant 3: HighlightColor is persisted as a numeric int by SavedFilterJsonConverter.
        // Reordering members, inserting a value mid-sequence, or changing an explicit `= N` assignment would
        // silently remap user-saved highlights to a different color.
        Assert.Equal(expectedOrdinal, (int)member);
    }

    [Fact]
    public void SavedFilter_BasicMode_Json_ContainsLiteralBasicFilterKey()
    {
        // Arrange
        var filter = SavedFilter.TryCreate(Constants.FilterIdEquals100, mode: FilterMode.Basic);
        Assert.NotNull(filter);
        Assert.NotNull(filter.BasicFilter);

        // Act
        string json = JsonSerializer.Serialize(filter);

        // Assert
        Assert.Contains("\"BasicFilter\"", json);
        Assert.DoesNotContain("\"StructuredFilter\"", json);
    }

    [Fact]
    public void SavedFilter_BasicMode_Read_AcceptsLiteralBasicFilterKey()
    {
        // Arrange — invariant 1 (read side): the converter must continue reading the literal "BasicFilter" key
        // so previously-persisted Basic-mode rows survive a rename pass intact.
        const string PersistedJson =
            $$"""
            {
              "Color": 0,
              "ComparisonText": "{{Constants.FilterIdEquals100}}",
              "IsExcluded": false,
              "Mode": "Basic",
              "BasicFilter": {
                "Comparison": { "Property": "Id", "Operator": "Equals", "MatchMode": "Single", "Value": "100", "Values": [] },
                "SubFilters": []
              }
            }
            """;

        // Act
        var restored = JsonSerializer.Deserialize<SavedFilter>(PersistedJson);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(FilterMode.Basic, restored.Mode);
        Assert.NotNull(restored.BasicFilter);
        Assert.Equal(EventProperty.Id, restored.BasicFilter.Comparison.Property);
        Assert.Equal("100", restored.BasicFilter.Comparison.Value);
    }

    [Theory]
    [InlineData(FilterMode.Basic, "\"Mode\":\"Basic\"")]
    [InlineData(FilterMode.Advanced, "\"Mode\":\"Advanced\"")]
    [InlineData(FilterMode.Cached, "\"Mode\":\"Cached\"")]
    public void SavedFilter_ModeJson_IsStringValuedForEveryMode(FilterMode mode, string expectedFragment)
    {
        // Arrange
        var filter = SavedFilter.TryCreate(Constants.FilterIdEquals100, mode: mode);
        Assert.NotNull(filter);

        // Act
        string json = JsonSerializer.Serialize(filter);

        // Assert — the converter writes Mode as Enum.ToString(); persisted strings must use the CLR member names
        // so legacy readers (and our own reader) can Enum.TryParse them back.
        Assert.Contains(expectedFragment, json);
    }

    [Theory]
    [InlineData(0, FilterMode.Advanced)]
    [InlineData(1, FilterMode.Basic)]
    [InlineData(2, FilterMode.Cached)]
    public void SavedFilter_NumericMode_IsAccepted_ByConverter(int numericMode, FilterMode expectedMode)
    {
        // Arrange — SavedFilterJsonConverter.ReadFilterMode accepts both string ("Basic") and numeric (1) modes
        // to tolerate persisted records from intermediate L4 builds. F16d must keep the numeric branch alive
        // (line 159 of SavedFilterJsonConverter) or those records silently fall back to FilterMode.Advanced.
        string json =
            $$"""
            {
              "Color": 0,
              "ComparisonText": "{{Constants.FilterIdEquals100}}",
              "IsExcluded": false,
              "Mode": {{numericMode}}
            }
            """;

        // Act
        var restored = JsonSerializer.Deserialize<SavedFilter>(json);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(expectedMode, restored.Mode);
    }

    [Fact]
    public void SavedFilter_StringHighlightColor_IsAccepted_ByConverter()
    {
        // Arrange — invariant 3 (read side, string branch): SavedFilterJsonConverter.ReadHighlightColor accepts
        // CLR enum names via Enum.TryParse (line 167). This characterization pins the read-side string branch so
        // F16d can't drop it. The legacy [EnumMember] slugs on EventProperty are NOT used for HighlightColor;
        // CLR names like "LightRed" are the only accepted string form.
        const string Json =
            $$"""
            {
              "Color": "LightRed",
              "ComparisonText": "{{Constants.FilterIdEquals100}}",
              "IsExcluded": false,
              "Mode": "Advanced"
            }
            """;

        // Act
        var restored = JsonSerializer.Deserialize<SavedFilter>(Json);

        // Assert
        Assert.NotNull(restored);
        Assert.Equal(HighlightColor.LightRed, restored.Color);
    }

    [Theory]
    [InlineData(FilterMode.Advanced)]
    [InlineData(FilterMode.Cached)]
    public void SavedFilter_Write_NonBasicMode_OmitsBasicFilterKey(FilterMode mode)
    {
        // Arrange — invariant 5 (write side): the converter only emits "BasicFilter" when value.BasicFilter is
        // not null (lines 146-150). Non-Basic modes force BasicFilter=null in TryCreate / LoadFromPersisted,
        // so the key must be absent. If F16d dropped the [JsonConverter] attribute and fell back to default-STJ,
        // "BasicFilter":null would land on disk for every Advanced/Cached row and the failure mode would only
        // surface as Basic-mode misidentification on next read.
        var filter = SavedFilter.TryCreate(Constants.FilterIdEquals100, mode: mode);
        Assert.NotNull(filter);
        Assert.Null(filter.BasicFilter);

        // Act
        string json = JsonSerializer.Serialize(filter);

        // Assert
        Assert.DoesNotContain("\"BasicFilter\"", json);
    }

    [Fact]
    public void SavedFilter_Write_OmitsJsonIgnoredProperties()
    {
        // Arrange — SavedFilter has [JsonIgnore] on Id, Compiled, and IsEnabled. The converter must omit them
        // from persisted output so loading never has to migrate around their absence. If F16d's rename or move
        // dropped the converter and fell back to default-STJ, these would still be omitted thanks to the
        // attributes; this test pins both the attribute presence and the converter's behavior together.
        var filter = SavedFilter.TryCreate(Constants.FilterIdEquals100, mode: FilterMode.Basic);
        Assert.NotNull(filter);

        // Act
        string json = JsonSerializer.Serialize(filter);

        // Assert — pin the property-name pattern (trailing colon) so the substring "Id" inside the hydrated
        // BasicFilter's "Property":"Id" payload doesn't trigger a false positive.
        Assert.DoesNotContain("\"Id\":", json);
        Assert.DoesNotContain("\"Compiled\":", json);
        Assert.DoesNotContain("\"IsEnabled\":", json);
    }

    [Fact]
    public void SavedFilter_Write_PinsNumericHighlightColor()
    {
        // Arrange — invariant 3 (write side): the converter writes Color as a number (writer.WriteNumber on
        // line 141). The read side accepts both numeric and string forms, so a silent switch to
        // writer.WriteString would NOT fail round-trip tests but WOULD change the on-disk shape for every new
        // record. Blue=17 is chosen because the ordinal is distinctive (no leading-zero ambiguity) and the value
        // is mid-enum so a member reorder would shift it.
        var filter = SavedFilter.TryCreate(
            Constants.FilterIdEquals100,
            color: HighlightColor.Blue,
            mode: FilterMode.Advanced);

        Assert.NotNull(filter);

        // Act
        string json = JsonSerializer.Serialize(filter);

        // Assert — literal numeric form, not "\"Color\":\"Blue\"".
        Assert.Contains("\"Color\":17", json);
        Assert.DoesNotContain("\"Color\":\"Blue\"", json);
    }

    [Fact]
    public void SavedFiltersStorageKey_LiteralValue_IsFrozenAcrossF16()
    {
        // Arrange — invariant 6: the MAUI Preferences.Default storage key "saved-filters" in PreferencesProvider
        // is part of the storage contract; renaming the key would silently strand every user's persisted filters.
        // PreferencesProvider lives in the MAUI head (EventLogExpert.csproj) which has no test project and no IVT
        // grant, so we pin the key by inspecting the source file directly. A repo-root marker (the .slnx file)
        // anchors the walk so the test is resilient to bin/ depth changes.
        string preferencesProviderPath = ResolveRepoRelativePath(
            "src",
            "EventLogExpert",
            "Services",
            "PreferencesProvider.cs");

        string source = File.ReadAllText(preferencesProviderPath);

        // Act + Assert — pin the constant declaration AND both call-through sites so a future search-and-replace
        // can't accidentally leave the constant intact while breaking the read/write usages.
        Assert.Matches(
            new Regex("""private\s+const\s+string\s+SavedFilters\s*=\s*"saved-filters"\s*;"""),
            source);

        Assert.Contains("Preferences.Default.Get(SavedFilters, \"[]\")", source);
        Assert.Contains("Preferences.Default.Set(SavedFilters, JsonSerializer.Serialize(value))", source);
    }

    private static string ResolveRepoRelativePath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EventLogExpert.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        string combined = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
        Assert.True(File.Exists(combined), $"Expected source file at {combined} to exist.");
        return combined;
    }
}
