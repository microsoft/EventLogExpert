// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization.Plural;
using System.Globalization;

namespace EventLogExpert.UI.Tests.Localization.Plural;

public sealed class IcuMessageFormatterTests
{
    private static readonly CultureInfo s_numberCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo s_pluralCulture = CultureInfo.GetCultureInfo("en-US");
    private readonly IcuMessageFormatter _formatter = new();

    [Fact]
    public void Format_WithArgumentAtBranchEndBeforeNestedClosers_ParsesAllClosers()
    {
        string actual = Format("{count, plural, other {{name}}}", Args(("count", 2), ("name", "done")));

        Assert.Equal("done", actual);
    }

    [Fact]
    public void Format_WithCommasInBranchText_PreservesCommas()
    {
        string actual = Format("{count, plural, other {alpha, beta, gamma}}", Args(("count", 2)));

        Assert.Equal("alpha, beta, gamma", actual);
    }

    [Fact]
    public void Format_WithDecimalSelector_PreservesVisibleFractionForCldrCategory()
    {
        string actual = Format("{count, plural, one {one item} other {items}}", Args(("count", 1.0m)));

        Assert.Equal("items", actual);
    }

    [Fact]
    public void Format_WithExactSelector_ChecksExactValueBeforeCldrCategory()
    {
        string actual = Format("{count, plural, =1 {single item} one {grammatical item} other {items}}", Args(("count", 1)));

        Assert.Equal("single item", actual);
    }

    [Fact]
    public void Format_WithLeadingTrailingBranchSpaces_PreservesBranchTextByteForByte()
    {
        string actual = Format("A{count, plural, other {  {name} , done  }}B", Args(("count", 2), ("name", "x")));

        Assert.Equal("A  x , done  B", actual);
    }

    [Theory]
    [InlineData("{count, plural, one {one}}")]
    [InlineData("{count, plural, one {one} one {again} other {other}}")]
    [InlineData("{count:0{0}}")]
    [InlineData("{count, plural, other {# files}}")]
    [InlineData("{count, plural, other {files}")]
    [InlineData("{count, plural, bogus {files} other {other}}")]
    [InlineData("{count, plural, =x {files} other {other}}")]
    public void Format_WithMalformedPattern_ThrowsIcuMessageException(string pattern) =>
        Assert.Throws<IcuMessageException>(() => Format(pattern, Args(("count", 2))));

    [Fact]
    public void Format_WithMissingArgument_ThrowsIcuMessageException() =>
        Assert.Throws<IcuMessageException>(() => Format("Hello {name}", Args()));

    [Theory]
    [InlineData(1, 1, "1 file has 1 duplicate")]
    [InlineData(1, 2, "1 file has 2 duplicates")]
    [InlineData(2, 1, "2 files have 1 duplicate")]
    [InlineData(2, 2, "2 files have 2 duplicates")]
    public void Format_WithNestedTwoCounts_SelectsBothLevels(int fileCount, int duplicateCount, string expected)
    {
        string pattern = "{fileCount, plural, one {{fileCount} file has {duplicateCount, plural, one {{duplicateCount} duplicate} other {{duplicateCount} duplicates}}} other {{fileCount} files have {duplicateCount, plural, one {{duplicateCount} duplicate} other {{duplicateCount} duplicates}}}}";

        Assert.Equal(expected, Format(pattern, Args(("fileCount", fileCount), ("duplicateCount", duplicateCount))));
    }

    [Fact]
    public void Format_WithNonIcuText_ReturnsInputVerbatim()
    {
        const string pattern = "No placeholders here.";

        Assert.Equal(pattern, Format(pattern, Args()));
    }

    [Fact]
    public void Format_WithNonNumericPluralSelector_ThrowsIcuMessageException() =>
        Assert.Throws<IcuMessageException>(() => Format("{count, plural, other {items}}", Args(("count", "two"))));

    [Theory]
    [InlineData(true)]
    [InlineData("1")]
    [InlineData('1')]
    public void Format_WithNonNumericSelectorType_ThrowsIcuMessageException(object selector) =>
        Assert.Throws<IcuMessageException>(() => Format("{count, plural, one {one} other {other}}", Args(("count", selector))));

    [Theory]
    [InlineData(1, "1 file")]
    [InlineData(2, "2 files")]
    public void Format_WithOneAndOtherBranches_SelectsCldrCategory(int count, string expected) =>
        Assert.Equal(expected, Format("{count, plural, one {{count} file} other {{count} files}}", Args(("count", count))));

    [Fact]
    public void Format_WithPositionalArgument_UsesStringKeyForPosition()
    {
        string actual = Format("Value {0}", Args(("0", 42)));

        Assert.Equal("Value 42", actual);
    }

    [Fact]
    public void Format_WithRawAndFormattedArguments_UsesNumberCultureAndNetFormat()
    {
        string actual = Format("raw {count}; grouped {count:N0}; custom {count:#,0}", Args(("count", 1000)));

        Assert.Equal("raw 1000; grouped 1,000; custom 1,000", actual);
    }

    [Fact]
    public void Format_WithRootLiteralBraces_RendersEscapedBraces()
    {
        string actual = Format("{{Start}} {count, plural, other {done}} end }}", Args(("count", 2)));

        Assert.Equal("{Start} done end }", actual);
    }

    [Fact]
    public void Format_WithSuppliedNull_RendersEmptyString()
    {
        string actual = Format("Hello {name}.", Args(("name", null)));

        Assert.Equal("Hello .", actual);
    }

    private static Dictionary<string, object?> Args(params (string Name, object? Value)[] args)
    {
        Dictionary<string, object?> result = new(StringComparer.Ordinal);
        foreach ((string name, object? value) in args)
        {
            result.Add(name, value);
        }

        return result;
    }

    private string Format(string pattern, IReadOnlyDictionary<string, object?> args) =>
        _formatter.Format(pattern, args, s_numberCulture, s_pluralCulture);
}
