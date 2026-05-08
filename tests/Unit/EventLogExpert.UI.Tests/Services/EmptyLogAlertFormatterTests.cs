// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Services;

namespace EventLogExpert.UI.Tests.Services;

public sealed class EmptyLogAlertFormatterTests
{
    [Fact]
    public void BuildMessage_EmptyList_ThrowsArgumentException()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            EmptyLogAlertFormatter.BuildMessage([]));

        Assert.Equal("displayNames", ex.ParamName);
    }

    [Fact]
    public void BuildMessage_NullList_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => EmptyLogAlertFormatter.BuildMessage(null!));
    }

    [Fact]
    public void BuildMessage_PreservesNameOrderInPluralCase()
    {
        string result = EmptyLogAlertFormatter.BuildMessage(["Zebra", "Apple", "Mango"]);

        Assert.Equal("3 logs contained no events: Zebra, Apple, Mango", result);
    }

    [Fact]
    public void BuildMessage_SingleName_UsesSingularPhrasing()
    {
        string result = EmptyLogAlertFormatter.BuildMessage(["Application.evtx"]);

        Assert.Equal("Log contains no events: Application.evtx", result);
    }

    [Fact]
    public void BuildMessage_ThreeNames_UsesPluralPhrasingWithCountAndOrderedJoin()
    {
        string result = EmptyLogAlertFormatter.BuildMessage(["A", "B", "C"]);

        Assert.Equal("3 logs contained no events: A, B, C", result);
    }

    [Fact]
    public void BuildMessage_TwoNames_UsesPluralPhrasingWithCommaJoin()
    {
        string result = EmptyLogAlertFormatter.BuildMessage(["A.evtx", "B.evtx"]);

        Assert.Equal("2 logs contained no events: A.evtx, B.evtx", result);
    }
}
