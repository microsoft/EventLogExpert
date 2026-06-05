// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.FilterLibrary;

namespace EventLogExpert.Runtime.Tests.FilterLibrary;

public sealed class LibraryEntryTagNormalizerTests
{
    [Fact]
    public void MigrateBackslashName_DeepPath_PromotesAllPrefixSegmentsToTags()
    {
        var entry = BuildFilterSet(@"a\b\c\d", []);

        var result = LibraryEntryTagNormalizer.MigrateBackslashName(entry);

        Assert.Equal("d", result.Name);
        Assert.Equal(["a", "b", "c"], result.Tags);
    }

    [Fact]
    public void MigrateBackslashName_IsIdempotent_SecondCallNoOp()
    {
        var entry = BuildFilterSet(@"Exchange\HUB", []);

        var firstPass = LibraryEntryTagNormalizer.MigrateBackslashName(entry);
        var secondPass = LibraryEntryTagNormalizer.MigrateBackslashName(firstPass);

        Assert.Same(firstPass, secondPass);
    }

    [Fact]
    public void MigrateBackslashName_LeadingAndTrailingBackslashes_DroppedByRemoveEmptyEntries()
    {
        var entry = BuildFilterSet(@"\Network\DNS\", []);

        var result = LibraryEntryTagNormalizer.MigrateBackslashName(entry);

        Assert.Equal("DNS", result.Name);
        Assert.Equal(["network"], result.Tags);
    }

    [Fact]
    public void MigrateBackslashName_NameWithBackslash_SplitsToFlatNameAndTags()
    {
        var entry = BuildFilterSet(@"Exchange\HUB", []);

        var result = LibraryEntryTagNormalizer.MigrateBackslashName(entry);

        Assert.Equal("HUB", result.Name);
        Assert.Equal(["exchange"], result.Tags);
    }

    [Fact]
    public void MigrateBackslashName_NameWithoutBackslash_ReturnsUnchanged()
    {
        var entry = BuildFilterSet("Sharepoint", []);

        var result = LibraryEntryTagNormalizer.MigrateBackslashName(entry);

        Assert.Same(entry, result);
    }

    [Fact]
    public void MigrateBackslashName_NullArg_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LibraryEntryTagNormalizer.MigrateBackslashName(null!));
    }

    [Fact]
    public void MigrateBackslashName_OnlyBackslashes_ReturnsUnchanged()
    {
        var entry = BuildFilterSet(@"\\\\", []);

        var result = LibraryEntryTagNormalizer.MigrateBackslashName(entry);

        Assert.Same(entry, result);
    }

    [Fact]
    public void MigrateBackslashName_PathLikeNameWithDriveLetter_ReturnsUnchanged()
    {
        var entry = BuildFilterSet(@"C:\Windows\System32", []);

        var result = LibraryEntryTagNormalizer.MigrateBackslashName(entry);

        Assert.Same(entry, result);
    }

    [Fact]
    public void MigrateBackslashName_PreservesExistingTagsAndUnionsWithSegments()
    {
        var entry = BuildFilterSet(@"Network\DNS", ["existing-tag"]);

        var result = LibraryEntryTagNormalizer.MigrateBackslashName(entry);

        Assert.Equal("DNS", result.Name);
        Assert.Contains("network", result.Tags);
        Assert.Contains("existing-tag", result.Tags);
    }

    [Fact]
    public void MigrateBackslashName_SavedFilterEntry_PreservesDerivedType()
    {
        var savedFilter = SavedFilter.TryCreate("Level == 4");
        Assert.NotNull(savedFilter);
        var entry = new LibraryEntrySavedFilter
        {
            Name = @"Network\DNS",
            CreatedUtc = DateTimeOffset.UtcNow,
            Filter = savedFilter,
        };

        var result = LibraryEntryTagNormalizer.MigrateBackslashName(entry);

        var typedResult = Assert.IsType<LibraryEntrySavedFilter>(result);
        Assert.Equal("DNS", typedResult.Name);
        Assert.Equal(["network"], typedResult.Tags);
        Assert.Same(savedFilter, typedResult.Filter);
    }

    [Fact]
    public void Normalize_CapsListAt20Items()
    {
        var tags = Enumerable.Range(1, 25).Select(i => $"tag{i}").ToList();

        var result = LibraryEntryTagNormalizer.Normalize(tags);

        Assert.Equal(20, result.Count);
        Assert.Equal("tag1", result[0]);
        Assert.Equal("tag20", result[^1]);
    }

    [Fact]
    public void Normalize_DedupesAfterNormalization()
    {
        var tags = new[] { "Foo", "foo", "  FOO  ", "BAR" };

        var result = LibraryEntryTagNormalizer.Normalize(tags);

        Assert.Equal(["foo", "bar"], result);
    }

    [Fact]
    public void Normalize_DropsEmptyOrWhitespaceTags()
    {
        var tags = new[] { "valid", "", "   ", "\t", "also-valid" };

        var result = LibraryEntryTagNormalizer.Normalize(tags);

        Assert.Equal(["valid", "also-valid"], result);
    }

    [Fact]
    public void Normalize_DropsNullEntries()
    {
        var tags = new[] { "valid", null!, "another" };

        var result = LibraryEntryTagNormalizer.Normalize(tags);

        Assert.Equal(["valid", "another"], result);
    }

    [Fact]
    public void Normalize_NullArg_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LibraryEntryTagNormalizer.Normalize(null!));
    }

    [Fact]
    public void Normalize_PreservesInputOrder()
    {
        var tags = new[] { "charlie", "alpha", "bravo" };

        var result = LibraryEntryTagNormalizer.Normalize(tags);

        Assert.Equal(["charlie", "alpha", "bravo"], result);
    }

    [Fact]
    public void Normalize_TrimsAndLowercases()
    {
        var tags = new[] { "  Exchange  ", "HuB" };

        var result = LibraryEntryTagNormalizer.Normalize(tags);

        Assert.Equal(["exchange", "hub"], result);
    }

    [Fact]
    public void Normalize_TruncatesTagsExceedingMaxLength()
    {
        var longTag = new string('a', LibraryEntryTagNormalizer.MaxTagLength + 10);

        var result = LibraryEntryTagNormalizer.Normalize([longTag]);

        Assert.Single(result);
        Assert.Equal(LibraryEntryTagNormalizer.MaxTagLength, result[0].Length);
    }

    private static LibraryEntryFilterSet BuildFilterSet(string name, IEnumerable<string> tags) =>
        new()
        {
            Name = name,
            CreatedUtc = DateTimeOffset.UtcNow,
            Filters = [],
            Tags = [.. tags],
        };
}
