// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Tests.TestUtils;
using System.Linq.Dynamic.Core.Exceptions;

namespace EventLogExpert.UI.Tests.Models;

public sealed class FilterComparisonTests
{
    [Fact]
    public void Expression_WhenCompoundCondition_ShouldMatchCorrectEvent()
    {
        // Arrange
        FilterComparison model = new() { Value = "Id == 100 && Level == \"Error\"" };
        DisplayEventModel matchingEvent = EventUtils.CreateTestEvent(100, level: "Error");
        DisplayEventModel nonMatchingIdEvent = EventUtils.CreateTestEvent(200, level: "Error");
        DisplayEventModel nonMatchingLevelEvent = EventUtils.CreateTestEvent(100, level: "Information");

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
        FilterComparison model = new() { Value = "ComputerName == \"SERVER01\"" };
        DisplayEventModel matchingEvent = EventUtils.CreateTestEvent(computerName: "SERVER01");
        DisplayEventModel nonMatchingEvent = EventUtils.CreateTestEvent(computerName: "SERVER02");

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
        FilterComparison model = new() { Value = "Description.Contains(\"error occurred\")" };

        DisplayEventModel matchingEvent =
            EventUtils.CreateTestEvent(description: "An error occurred in the application");

        DisplayEventModel nonMatchingEvent =
            EventUtils.CreateTestEvent(description: "Operation completed successfully");

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
        FilterComparison model = new() { Value = "Id == 100" };
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
        FilterComparison model = new() { Value = "Id > 100" };
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
        FilterComparison model = new() { Value = "Id != 100" };
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
        FilterComparison model = new() { Value = "Level == \"Error\"" };
        DisplayEventModel matchingEvent = EventUtils.CreateTestEvent(level: "Error");
        DisplayEventModel nonMatchingEvent = EventUtils.CreateTestEvent(level: "Information");

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
        FilterComparison model = new() { Value = "Id == 100 || Id == 200" };
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
        FilterComparison model = new() { Value = "Source.Contains(\"Test\")" };
        DisplayEventModel matchingEvent = EventUtils.CreateTestEvent(source: "TestSource");
        DisplayEventModel nonMatchingEvent = EventUtils.CreateTestEvent(source: "OtherSource");

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
        FilterComparison model = new() { Value = "TaskCategory.Contains(\"Security\")" };
        DisplayEventModel matchingEvent = EventUtils.CreateTestEvent(taskCategory: "Security Audit");
        DisplayEventModel nonMatchingEvent = EventUtils.CreateTestEvent(taskCategory: "System");

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
        model.Value = "Id == 100";

        // Assert
        Assert.NotNull(model.Expression);
    }

    [Fact]
    public void Expression_WhenValueUpdated_ShouldUpdateExpression()
    {
        // Arrange
        FilterComparison model = new() { Value = "Id == 100" };
        DisplayEventModel event100 = EventUtils.CreateTestEvent(100);
        DisplayEventModel event200 = EventUtils.CreateTestEvent(200);

        // Act
        bool initialMatch100 = model.Expression(event100);
        bool initialMatch200 = model.Expression(event200);
        model.Value = "Id == 200";
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
        Assert.Throws<ParseException>(() => model.Value = "InvalidProperty == 100");
    }

    [Fact]
    public void Value_WhenNotValid_ShouldThrow()
    {
        // Arrange
        FilterComparison model = new();

        // Act & Assert
        Assert.Throws<ParseException>(() => model.Value = "Id == invalid");
    }
}
