// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using EventLogExpert.Localization;
using EventLogExpert.Runtime.DetailsPane;
using EventLogExpert.UI.DetailsPane;
using EventLogExpert.UI.Tests.TestUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Tests.DetailsPane;

public sealed class DetailsFieldRowLocalizerWiringTests : BunitContext
{
    public DetailsFieldRowLocalizerWiringTests()
    {
        Services.AddSingleton<IStringLocalizer<SharedResource>>(new MarkerLocalizer());
    }

    [Theory]
    [InlineData(PlaceholderKind.Empty, "Details_Placeholder_Empty")]
    [InlineData(PlaceholderKind.NoValues, "Details_Placeholder_NoValues")]
    [InlineData(PlaceholderKind.NullValue, "Details_Placeholder_NullValue")]
    public void EachPlaceholderKind_RendersItsOwnLocalizerKey(PlaceholderKind kind, string expectedKey)
    {
        DetailsField field = new()
        {
            Label = "SubjectUserName",
            PreviewLines = ["(placeholder)"],
            FullLines = ["(placeholder)"],
            CopyValue = string.Empty,
            IsMuted = true,
            Placeholder = kind
        };

        var cut = Render<DetailsFieldRow>(parameters => parameters.Add(row => row.Field, field));

        Assert.Contains($"[[{expectedKey}]]", cut.Markup);
    }

    [Fact]
    public void PlaceholderDescriptionAndAria_AreDrivenByTheLocalizer()
    {
        DetailsField field = new()
        {
            Label = "SubjectUserName",
            PreviewLines = ["(empty)"],
            FullLines = ["(empty)"],
            CopyValue = string.Empty,
            IsMuted = true,
            Explanation = GlossaryTerm.SubjectUserName4624,
            Placeholder = PlaceholderKind.Empty
        };

        var cut = Render<DetailsFieldRow>(parameters => parameters.Add(row => row.Field, field));

        Assert.Equal("[[Details_CopyValueAria(SubjectUserName)]]", cut.Find(".details-copy").GetAttribute("aria-label"));
        Assert.Contains("[[Details_Placeholder_Empty]]", cut.Markup);
        Assert.Contains("[[Explain_SubjectUserName4624]]", cut.Markup);
        Assert.DoesNotContain("(empty)", cut.Markup);
    }

    [Fact]
    public void RealLocalizer_DoesNotLeakResourceKeys()
    {
        Services.RemoveAll<IStringLocalizer<SharedResource>>();
        Services.AddEventLogLocalization();
        DetailsField field = new()
        {
            Label = "SubjectUserName",
            PreviewLines = ["(empty)"],
            FullLines = ["(empty)"],
            CopyValue = string.Empty,
            IsMuted = true,
            Placeholder = PlaceholderKind.Empty
        };

        var cut = Render<DetailsFieldRow>(parameters => parameters.Add(row => row.Field, field));

        Assert.DoesNotContain("Details_", cut.Markup);
    }

    [Fact]
    public void ShowMoreToggle_IsDrivenByTheLocalizer()
    {
        DetailsField field = new()
        {
            Label = "CommandLine",
            PreviewLines = ["short"],
            FullLines = ["long"],
            CopyValue = "long",
            IsTruncated = true
        };

        var cut = Render<DetailsFieldRow>(parameters => parameters
            .Add(row => row.Field, field)
            .Add(row => row.IsExpanded, false));

        Assert.Contains("[[Details_ShowMore]]", cut.Markup);
    }
}
