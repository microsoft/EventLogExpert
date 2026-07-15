// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.UI.LogTable.Find;
using Microsoft.AspNetCore.Components.Web;

namespace EventLogExpert.UI.Tests.LogTable;

public sealed class FindBarTests : BunitContext
{
    public FindBarTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/LogTable/Find/FindBar.razor.js");
    }

    [Fact]
    public void CaseToggle_InOptionsTray_TogglesCaseSensitive()
    {
        bool? toggled = null;
        var cut = Render<FindBar>(parameters => parameters
            .Add(p => p.Query, "x")
            .Add(p => p.CaseSensitive, false)
            .Add(p => p.CaseSensitiveChanged, value => toggled = value));

        cut.Find(".find-options-toggle").Click();
        cut.FindAll(".find-options .toggle-input")[0].Change(true);

        Assert.True(toggled);
    }

    [Fact]
    public void CountText_EmptyQuery_IsBlank()
    {
        var cut = Render<FindBar>(parameters => parameters.Add(p => p.Query, string.Empty));

        Assert.Equal(string.Empty, cut.Find(".find-count").TextContent.Trim());
    }

    [Fact]
    public void CountText_NoMatches_ShowsNoResults()
    {
        var cut = Render<FindBar>(parameters => parameters
            .Add(p => p.Query, "x")
            .Add(p => p.IsScanning, false)
            .Add(p => p.MatchCount, 0));

        Assert.Equal("No results", cut.Find(".find-count").TextContent.Trim());
    }

    [Fact]
    public void CountText_Scanning_ShowsSearching()
    {
        var cut = Render<FindBar>(parameters => parameters
            .Add(p => p.Query, "x")
            .Add(p => p.IsScanning, true));

        Assert.Equal("Searching\u2026", cut.Find(".find-count").TextContent.Trim());
    }

    [Fact]
    public void CountText_WithMatches_ShowsOrdinalOfTotal()
    {
        var cut = Render<FindBar>(parameters => parameters
            .Add(p => p.Query, "x")
            .Add(p => p.IsScanning, false)
            .Add(p => p.MatchCount, 57)
            .Add(p => p.CurrentOrdinal, 3));

        Assert.Equal("3/57", cut.Find(".find-count").TextContent.Trim());
    }

    [Fact]
    public void Enter_InvokesNext_ShiftEnter_InvokesPrevious()
    {
        int next = 0;
        int previous = 0;
        var cut = Render<FindBar>(parameters => parameters
            .Add(p => p.Query, "x")
            .Add(p => p.OnNext, () => next++)
            .Add(p => p.OnPrevious, () => previous++));

        cut.Find(".find-input").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Find(".find-input").KeyDown(new KeyboardEventArgs { Key = "Enter", ShiftKey = true });

        Assert.Equal(1, next);
        Assert.Equal(1, previous);
    }

    [Fact]
    public void Escape_InvokesOnClose()
    {
        int closed = 0;
        var cut = Render<FindBar>(parameters => parameters
            .Add(p => p.Query, "x")
            .Add(p => p.OnClose, () => closed++));

        cut.Find(".find-bar").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(1, closed);
    }

    [Fact]
    public void F3_InvokesNext_ShiftF3_InvokesPrevious()
    {
        int next = 0;
        int previous = 0;
        var cut = Render<FindBar>(parameters => parameters
            .Add(p => p.Query, "x")
            .Add(p => p.OnNext, () => next++)
            .Add(p => p.OnPrevious, () => previous++));

        cut.Find(".find-bar").KeyDown(new KeyboardEventArgs { Key = "F3" });
        cut.Find(".find-bar").KeyDown(new KeyboardEventArgs { Key = "F3", ShiftKey = true });

        Assert.Equal(1, next);
        Assert.Equal(1, previous);
    }

    [Fact]
    public void Input_InvokesQueryChanged()
    {
        string? typed = null;
        var cut = Render<FindBar>(parameters => parameters
            .Add(p => p.Query, string.Empty)
            .Add(p => p.QueryChanged, value => typed = value));

        cut.Find(".find-input").Input("error");

        Assert.Equal("error", typed);
    }

    [Fact]
    public void NavButtons_DisabledWhileScanning_AndWhenNoMatches()
    {
        var scanning = Render<FindBar>(parameters => parameters
            .Add(p => p.Query, "x")
            .Add(p => p.IsScanning, true)
            .Add(p => p.MatchCount, 5));

        Assert.True(scanning.FindAll(".find-nav").All(button => button.HasAttribute("disabled")));

        var noMatches = Render<FindBar>(parameters => parameters
            .Add(p => p.Query, "x")
            .Add(p => p.IsScanning, false)
            .Add(p => p.MatchCount, 0));

        Assert.True(noMatches.FindAll(".find-nav").All(button => button.HasAttribute("disabled")));
    }

    [Fact]
    public void NavButtons_EnabledWithMatches()
    {
        var cut = Render<FindBar>(parameters => parameters
            .Add(p => p.Query, "x")
            .Add(p => p.IsScanning, false)
            .Add(p => p.MatchCount, 5)
            .Add(p => p.CurrentOrdinal, 1));

        Assert.True(cut.FindAll(".find-nav").All(button => !button.HasAttribute("disabled")));
    }

    [Fact]
    public void OptionsButton_ShowsActiveAccent_OnlyWhenAConstraintIsOn()
    {
        var inactive = Render<FindBar>(parameters => parameters
            .Add(p => p.Query, "x")
            .Add(p => p.CaseSensitive, false)
            .Add(p => p.WholeWord, false));

        Assert.DoesNotContain("is-active", inactive.Find(".find-options-toggle").ClassName);

        var active = Render<FindBar>(parameters => parameters
            .Add(p => p.Query, "x")
            .Add(p => p.WholeWord, true));

        Assert.Contains("is-active", active.Find(".find-options-toggle").ClassName);
    }

    [Fact]
    public void OptionsTray_CollapsedByDefault_ExpandsOnClick()
    {
        var cut = Render<FindBar>(parameters => parameters.Add(p => p.Query, "x"));

        Assert.Empty(cut.FindAll(".find-options"));
        Assert.Equal("false", cut.Find(".find-options-toggle").GetAttribute("aria-expanded"));
        Assert.False(cut.Find(".find-options-toggle").HasAttribute("aria-controls"));

        cut.Find(".find-options-toggle").Click();

        Assert.Single(cut.FindAll(".find-options"));
        Assert.Equal("true", cut.Find(".find-options-toggle").GetAttribute("aria-expanded"));
        Assert.Equal("find-options", cut.Find(".find-options-toggle").GetAttribute("aria-controls"));
    }

    [Fact]
    public void WholeWordToggle_InOptionsTray_TogglesWholeWord()
    {
        bool? toggled = null;
        var cut = Render<FindBar>(parameters => parameters
            .Add(p => p.Query, "x")
            .Add(p => p.WholeWord, false)
            .Add(p => p.WholeWordChanged, value => toggled = value));

        cut.Find(".find-options-toggle").Click();
        cut.FindAll(".find-options .toggle-input")[1].Change(true);

        Assert.True(toggled);
    }
}
