// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Runtime.Common.Display;

namespace EventLogExpert.Runtime.Tests.Common.Display;

/// <summary>
///     Locks the Basic filter editor's property dropdown contract: ordered alphabetically by display text (
///     <see cref="DisplayExtensions.ToFullString" />), and a new comparison/draft defaults its property to Event ID.
/// </summary>
public sealed class FilterPropertyDisplayOrderTests
{
    [Fact]
    public void PropertyDropdown_DefaultComparison_IsEventId() =>
        Assert.Equal(EventProperty.Id, new FilterComparison().Property);

    [Fact]
    public void PropertyDropdown_DefaultDraft_IsEventId() =>
        Assert.Equal(EventProperty.Id, new FilterComparisonDraft().Property);

    [Fact]
    public void PropertyDropdown_OrdersAlphabeticallyByDisplayText()
    {
        var ordered = Enum.GetValues<EventProperty>()
            .OrderBy(property => property.ToFullString(), StringComparer.CurrentCulture)
            .Select(property => property.ToFullString());

        Assert.Equal(
            [
                "Activity ID",
                "Description",
                "Event Data",
                "Event ID",
                "Keywords",
                "Level",
                "Log Name",
                "Opcode",
                "Process ID",
                "Related Activity ID",
                "Source",
                "Task Category",
                "Thread ID",
                "User Data",
                "User ID",
                "Xml"
            ],
            ordered);
    }
}
