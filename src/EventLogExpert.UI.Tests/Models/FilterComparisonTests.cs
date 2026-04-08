// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;
using System.Linq.Dynamic.Core.Exceptions;

namespace EventLogExpert.UI.Tests.Models;

public sealed class FilterComparisonTests
{
    [Fact]
    public void Expression_WhenCompoundCondition_ShouldMatchCorrectEvent()
    {
        // Arrange
        FilterComparison model = new() { Value = Constants.FilterIdEquals100AndLevelError };
        DisplayEventModel matchingEvent = EventUtils.CreateTestEvent(100, level: Constants.EventLevelError);
        DisplayEventModel nonMatchingIdEvent = EventUtils.CreateTestEvent(200, level: Constants.EventLevelError);
        DisplayEventModel nonMatchingLevelEvent = EventUtils.CreateTestEvent(100, level: Constants.EventLevelInformation);

        // Act
        bool matchResult = model.Expression(matchingEvent);
        bool nonMatchIdResult = model.Expression(nonMatchingIdEvent);
        bool nonMatchLevelResult = model.Expression(nonMatchingLevelEvent);

        // Assert
        Assert.True(matchResult);
        Assert.False(nonMatchIdResult);
        Assert.False(nonMatchLevelResult);
    }

    [Fact]
    public void Expression_WhenComputerNameEquals_ShouldMatchCorrectEvent()
    {
        // Arrange
        FilterComparison model = new() { Value = Constants.FilterComputerNameEqualsServer01 };
        DisplayEventModel matchingEvent = EventUtils.CreateTestEvent(computerName: Constants.EventComputerServer01);
        DisplayEventModel nonMatchingEvent = EventUtils.CreateTestEvent(computerName: Constants.EventComputerServer02);

        // Act
        bool matchResult = model.Expression(matchingEvent);
        bool nonMatchResult = model.Expression(nonMatchingEvent);

        // Assert
        Assert.True(matchResult);
        Assert.False(nonMatchResult);
    }

    [Fact]
    public void Expression_WhenDescriptionContains_ShouldMatchCorrectEvent()
    {
        // Arrange
        FilterComparison model = new() { Value = Constants.FilterDescriptionContainsErrorOccurred };

        DisplayEventModel matchingEvent =
            EventUtils.CreateTestEvent(description: Constants.EventDescriptionErrorOccurred);

        DisplayEventModel nonMatchingEvent =
            EventUtils.CreateTestEvent(description: Constants.EventDescriptionSuccess);

        // Act
        bool matchResult = model.Expression(matchingEvent);
        bool nonMatchResult = model.Expression(nonMatchingEvent);

        // Assert
        Assert.True(matchResult);
        Assert.False(nonMatchResult);
    }

    [Fact]
    public void Expression_WhenIdEquals_ShouldMatchCorrectEvent()
    {
        // Arrange
        FilterComparison model = new() { Value = Constants.FilterIdEquals100 };
        DisplayEventModel matchingEvent = EventUtils.CreateTestEvent(100);
        DisplayEventModel nonMatchingEvent = EventUtils.CreateTestEvent(200);

        // Act
        bool matchResult = model.Expression(matchingEvent);
        bool nonMatchResult = model.Expression(nonMatchingEvent);

        // Assert
        Assert.True(matchResult);
        Assert.False(nonMatchResult);
    }

    [Fact]
    public void Expression_WhenIdGreaterThan_ShouldMatchCorrectEvent()
    {
        // Arrange
        FilterComparison model = new() { Value = Constants.FilterIdGreaterThan100 };
        DisplayEventModel matchingEvent = EventUtils.CreateTestEvent(150);
        DisplayEventModel nonMatchingEvent = EventUtils.CreateTestEvent(50);

        // Act
        bool matchResult = model.Expression(matchingEvent);
        bool nonMatchResult = model.Expression(nonMatchingEvent);

        // Assert
        Assert.True(matchResult);
        Assert.False(nonMatchResult);
    }

    [Fact]
    public void Expression_WhenIdNotEquals_ShouldMatchCorrectEvent()
    {
        // Arrange
        FilterComparison model = new() { Value = Constants.FilterIdNotEquals100 };
        DisplayEventModel matchingEvent = EventUtils.CreateTestEvent(200);
        DisplayEventModel nonMatchingEvent = EventUtils.CreateTestEvent(100);

        // Act
        bool matchResult = model.Expression(matchingEvent);
        bool nonMatchResult = model.Expression(nonMatchingEvent);

        // Assert
        Assert.True(matchResult);
        Assert.False(nonMatchResult);
    }

    [Fact]
    public void Expression_WhenLevelEquals_ShouldMatchCorrectEvent()
    {
        // Arrange
        FilterComparison model = new() { Value = Constants.FilterLevelEqualsError };
        DisplayEventModel matchingEvent = EventUtils.CreateTestEvent(level: Constants.EventLevelError);
        DisplayEventModel nonMatchingEvent = EventUtils.CreateTestEvent(level: Constants.EventLevelInformation);

        // Act
        bool matchResult = model.Expression(matchingEvent);
        bool nonMatchResult = model.Expression(nonMatchingEvent);

        // Assert
        Assert.True(matchResult);
        Assert.False(nonMatchResult);
    }

    [Fact]
    public void Expression_WhenOrCondition_ShouldMatchCorrectEvent()
    {
        // Arrange
        FilterComparison model = new() { Value = Constants.FilterIdEquals100Or200 };
        DisplayEventModel matchingEvent1 = EventUtils.CreateTestEvent(100);
        DisplayEventModel matchingEvent2 = EventUtils.CreateTestEvent(200);
        DisplayEventModel nonMatchingEvent = EventUtils.CreateTestEvent(300);

        // Act
        bool match1Result = model.Expression(matchingEvent1);
        bool match2Result = model.Expression(matchingEvent2);
        bool nonMatchResult = model.Expression(nonMatchingEvent);

        // Assert
        Assert.True(match1Result);
        Assert.True(match2Result);
        Assert.False(nonMatchResult);
    }

    [Fact]
    public void Expression_WhenSourceContains_ShouldMatchCorrectEvent()
    {
        // Arrange
        FilterComparison model = new() { Value = Constants.FilterSourceContainsTest };
        DisplayEventModel matchingEvent = EventUtils.CreateTestEvent(source: Constants.EventSourceTestSource);
        DisplayEventModel nonMatchingEvent = EventUtils.CreateTestEvent(source: Constants.EventSourceOtherSource);

        // Act
        bool matchResult = model.Expression(matchingEvent);
        bool nonMatchResult = model.Expression(nonMatchingEvent);

        // Assert
        Assert.True(matchResult);
        Assert.False(nonMatchResult);
    }

    [Fact]
    public void Expression_WhenTaskCategoryContains_ShouldMatchCorrectEvent()
    {
        // Arrange
        FilterComparison model = new() { Value = Constants.FilterTaskCategoryContainsSecurity };
        DisplayEventModel matchingEvent = EventUtils.CreateTestEvent(taskCategory: Constants.EventTaskCategorySecurity);
        DisplayEventModel nonMatchingEvent = EventUtils.CreateTestEvent(taskCategory: Constants.EventTaskCategorySystem);

        // Act
        bool matchResult = model.Expression(matchingEvent);
        bool nonMatchResult = model.Expression(nonMatchingEvent);

        // Assert
        Assert.True(matchResult);
        Assert.False(nonMatchResult);
    }

    [Fact]
    public void Expression_WhenValidValue_ShouldContainFunc()
    {
        // Arrange
        FilterComparison model = new();

        // Act
        model.Value = Constants.FilterIdEquals100;

        // Assert
        Assert.NotNull(model.Expression);
    }

    [Fact]
    public void Expression_WhenValueUpdated_ShouldUpdateExpression()
    {
        // Arrange
        FilterComparison model = new() { Value = Constants.FilterIdEquals100 };
        DisplayEventModel event100 = EventUtils.CreateTestEvent(100);
        DisplayEventModel event200 = EventUtils.CreateTestEvent(200);

        // Act
        bool initialMatch100 = model.Expression(event100);
        bool initialMatch200 = model.Expression(event200);
        model.Value = Constants.FilterIdEquals200;
        bool updatedMatch100 = model.Expression(event100);
        bool updatedMatch200 = model.Expression(event200);

        // Assert
        Assert.True(initialMatch100);
        Assert.False(initialMatch200);
        Assert.False(updatedMatch100);
        Assert.True(updatedMatch200);
    }

    [Fact]
    public void Value_WhenEmptyString_ShouldThrow()
    {
        // Arrange
        FilterComparison model = new();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => model.Value = "");
    }

    [Fact]
    public void Value_WhenInvalidPropertyName_ShouldThrow()
    {
        // Arrange
        FilterComparison model = new();

        // Act & Assert
        Assert.Throws<ParseException>(() => model.Value = Constants.FilterInvalidProperty);
    }

    [Fact]
    public void Value_WhenNotValid_ShouldThrow()
    {
        // Arrange
        FilterComparison model = new();

        // Act & Assert
        Assert.Throws<ParseException>(() => model.Value = Constants.FilterInvalidValue);
    }
}
