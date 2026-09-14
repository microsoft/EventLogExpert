// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.FilterLibrary;
using EventLogExpert.UI.Tests.Localization;
using EventLogExpert.UI.Tests.TestUtils;
using System.Xml.Linq;

namespace EventLogExpert.UI.Tests.FilterLibrary;

public sealed class LibraryTabLocalizerTests
{
    private readonly MarkerLocalizer _localizer = new();

    public static TheoryData<LibraryTab, string> TabKeys() => new()
    {
        { LibraryTab.Saved, "LibraryTab_Saved" },
        { LibraryTab.Favorites, "LibraryTab_Favorites" },
        { LibraryTab.PreviouslyUsed, "LibraryTab_PreviouslyUsed" }
    };

    [Fact]
    public void Label_HandlesEveryLibraryTabMember()
    {
        LibraryTab[] mapped = [LibraryTab.Saved, LibraryTab.Favorites, LibraryTab.PreviouslyUsed];

        Assert.Equal(Enum.GetValues<LibraryTab>().OrderBy(tab => tab), mapped);
    }

    [Theory]
    [MemberData(nameof(TabKeys))]
    public void Label_RoutesEveryLibraryTabToExpectedKey(LibraryTab tab, string expectedKey) =>
        Assert.Equal($"[[{expectedKey}]]", LibraryTabLocalizer.Label(_localizer, tab));

    [Fact]
    public void Label_UnknownTab_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LibraryTabLocalizer.Label(_localizer, (LibraryTab)999));

    [Theory]
    [MemberData(nameof(TabKeys))]
    public void NeutralTabValues_KeepByteIdenticalEnglish(LibraryTab tab, string expectedKey) =>
        Assert.Equal(tab == LibraryTab.PreviouslyUsed ? "Previously Used" : tab.ToString(), NeutralValue(expectedKey));

    private static string NeutralValue(string key) =>
        XDocument.Load(LocalizationSourceScan.ResxPath)
            .Root!
            .Elements("data")
            .Single(element => (string?)element.Attribute("name") == key)
            .Element("value")!
            .Value;
}
