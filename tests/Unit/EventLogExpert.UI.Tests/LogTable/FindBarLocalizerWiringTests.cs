// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Localization;
using EventLogExpert.UI.LogTable.Find;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Tests.LogTable;

public sealed class FindBarLocalizerWiringTests : BunitContext
{
    public FindBarLocalizerWiringTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("./_content/EventLogExpert.UI/LogTable/Find/FindBar.razor.js");
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());
    }

    [Fact]
    public void CountText_NoMatches_UsesNoResultsKey()
    {
        var cut = Render<FindBar>(parameters => parameters
            .Add(p => p.Query, "x")
            .Add(p => p.IsScanning, false)
            .Add(p => p.MatchCount, 0));

        Assert.Equal("[[FindBar_NoResults]]", cut.Find(".find-count").TextContent.Trim());
    }

    [Fact]
    public void CountText_Scanning_UsesSearchingKey()
    {
        var cut = Render<FindBar>(parameters => parameters
            .Add(p => p.Query, "x")
            .Add(p => p.IsScanning, true));

        Assert.Equal("[[FindBar_Searching]]", cut.Find(".find-count").TextContent.Trim());
    }

    [Fact]
    public void CountText_WithMatches_UsesBareCountFormatKey()
    {
        var cut = Render<FindBar>(parameters => parameters
            .Add(p => p.Query, "x")
            .Add(p => p.IsScanning, false)
            .Add(p => p.MatchCount, 57)
            .Add(p => p.CurrentOrdinal, 3));

        Assert.Equal("[[FindBar_CountFormat]]", cut.Find(".find-count").TextContent.Trim());
    }

    [Fact]
    public void LabelsAndButtons_AreDrivenByTheLocalizer()
    {
        var cut = Render<FindBar>(parameters => parameters.Add(component => component.Query, "x"));

        Assert.Equal("[[FindBar_FindInEvents]]", cut.Find(".find-input").GetAttribute("aria-label"));
        Assert.Equal("[[FindBar_FindInEvents]]", cut.Find(".find-input").GetAttribute("placeholder"));
        Assert.Equal("[[FindBar_PreviousMatch]]", cut.FindAll(".find-nav")[0].GetAttribute("aria-label"));
        Assert.Equal("[[FindBar_NextMatch]]", cut.FindAll(".find-nav")[1].GetAttribute("aria-label"));
        Assert.Equal("[[FindBar_SearchOptions]]", cut.Find(".find-options-toggle").GetAttribute("aria-label"));
        Assert.Equal("[[FindBar_Close]]", cut.Find(".find-close").GetAttribute("aria-label"));

        cut.Find(".find-options-toggle").Click();

        Assert.Equal("[[FindBar_MatchCase]]", cut.Find("label[for=find-opt-case]").TextContent);
        Assert.Equal("[[FindBar_MatchWholeWord]]", cut.Find("label[for=find-opt-word]").TextContent);
    }
}
