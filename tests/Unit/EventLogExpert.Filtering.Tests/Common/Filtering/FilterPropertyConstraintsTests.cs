// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;

namespace EventLogExpert.Filtering.Tests.Common.Filtering;

public sealed class FilterPropertyConstraintsTests
{
    [Theory]
    [InlineData(EventProperty.ActivityId)]
    [InlineData(EventProperty.RelatedActivityId)]
    public void IsGuidValued_GuidProperties_ReturnTrue(EventProperty property) =>
        Assert.True(FilterPropertyConstraints.IsGuidValued(property));

    [Theory]
    [InlineData(EventProperty.Source)]
    [InlineData(EventProperty.Id)]
    [InlineData(EventProperty.LogName)]
    [InlineData(EventProperty.TaskCategory)]
    [InlineData(EventProperty.Opcode)]
    [InlineData(EventProperty.EventData)]
    public void IsGuidValued_NonGuidProperties_ReturnFalse(EventProperty property) =>
        Assert.False(FilterPropertyConstraints.IsGuidValued(property));
}
