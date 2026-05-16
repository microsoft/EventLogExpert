// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.UI.Filters;

namespace EventLogExpert.UI.Tests.Filters;

public sealed class HighlightColorExtensionsTests
{
    private static readonly IReadOnlyDictionary<HighlightColor, string> s_expectedCssNames = new Dictionary<HighlightColor, string>
    {
        [HighlightColor.LightRed] = "lightred",
        [HighlightColor.Red] = "red",
        [HighlightColor.DarkRed] = "darkred",
        [HighlightColor.LightOrange] = "lightorange",
        [HighlightColor.Orange] = "orange",
        [HighlightColor.DarkOrange] = "darkorange",
        [HighlightColor.LightYellow] = "lightyellow",
        [HighlightColor.Yellow] = "yellow",
        [HighlightColor.DarkYellow] = "darkyellow",
        [HighlightColor.LightGreen] = "lightgreen",
        [HighlightColor.Green] = "green",
        [HighlightColor.DarkGreen] = "darkgreen",
        [HighlightColor.LightTeal] = "lightteal",
        [HighlightColor.Teal] = "teal",
        [HighlightColor.DarkTeal] = "darkteal",
        [HighlightColor.LightBlue] = "lightblue",
        [HighlightColor.Blue] = "blue",
        [HighlightColor.DarkBlue] = "darkblue",
        [HighlightColor.LightPurple] = "lightpurple",
        [HighlightColor.Purple] = "purple",
        [HighlightColor.DarkPurple] = "darkpurple",
        [HighlightColor.LightMagenta] = "lightmagenta",
        [HighlightColor.Magenta] = "magenta",
        [HighlightColor.DarkMagenta] = "darkmagenta",
        [HighlightColor.LightPink] = "lightpink",
        [HighlightColor.Pink] = "pink",
        [HighlightColor.DarkPink] = "darkpink"
    };

    public static TheoryData<HighlightColor, string> KnownColorCssNamePairs()
    {
        var data = new TheoryData<HighlightColor, string>();

        foreach (var pair in s_expectedCssNames)
        {
            data.Add(pair.Key, pair.Value);
        }

        return data;
    }

    [Fact]
    public void EnumMembers_WhenCheckedAgainstTestFixture_ShouldAllAppearInExpectedCssNames()
    {
        // Arrange
        var declared = Enum.GetValues<HighlightColor>()
            .Where(c => c != HighlightColor.None)
            .ToHashSet();

        // Act
        var fixtureKeys = s_expectedCssNames.Keys.ToHashSet();

        // Assert
        Assert.Equal(declared, fixtureKeys);
    }

    [Fact]
    public void ToCssName_WhenCalledRepeatedly_ShouldReturnInternedReference()
    {
        // Act
        var first = HighlightColor.Red.ToCssName();
        var second = HighlightColor.Red.ToCssName();

        // Assert
        Assert.Same(first, second);
    }

    [Theory]
    [MemberData(nameof(KnownColorCssNamePairs))]
    public void ToCssName_WhenColorIsKnown_ShouldMatchCssPaletteName(HighlightColor color, string expectedCssName)
    {
        // Act
        var actual = color.ToCssName();

        // Assert
        Assert.Equal(expectedCssName, actual);
    }

    [Fact]
    public void ToCssName_WhenColorIsNone_ShouldReturnNull()
    {
        // Act
        var actual = HighlightColor.None.ToCssName();

        // Assert
        Assert.Null(actual);
    }

    [Fact]
    public void ToCssName_WhenColorIsUndefinedCast_ShouldReturnNull()
    {
        // Arrange
        var undefined = (HighlightColor)999;

        // Act
        var actual = undefined.ToCssName();

        // Assert
        Assert.Null(actual);
    }
}
