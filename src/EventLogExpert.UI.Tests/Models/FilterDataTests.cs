using EventLogExpert.UI.Models;
using EventLogExpert.UI.Tests.TestUtils.Constants;

namespace EventLogExpert.UI.Tests.Models;

public sealed class FilterDataTests
{
    [Fact]
    public void Category_WhenChanged_ShouldClearValue()
    {
        // Arrange
        FilterData model = new() { Category = FilterCategory.Id, Value = Constants.FilterValue100 };

        // Act
        model.Category = FilterCategory.Level;

        // Assert
        Assert.Null(model.Value);
    }

    [Fact]
    public void Category_WhenChanged_ShouldClearValues()
    {
        // Arrange
        FilterData model = new() { Category = FilterCategory.Id, Values = [Constants.FilterValue100, Constants.FilterValue1000] };

        // Act
        model.Category = FilterCategory.Level;

        // Assert
        Assert.Empty(model.Values);
    }

    [Fact]
    public void Category_WhenChanged_ShouldUpdateCategory()
    {
        // Arrange
        FilterData model = new() { Category = FilterCategory.Id };

        // Act
        model.Category = FilterCategory.Source;

        // Assert
        Assert.Equal(FilterCategory.Source, model.Category);
    }

    [Fact]
    public void Category_WhenChangedMultipleTimes_ShouldClearValueEachTime()
    {
        // Arrange
        FilterData model = new() { Category = FilterCategory.Id, Value = Constants.FilterValue100 };

        // Act & Assert - First change
        model.Category = FilterCategory.Level;
        Assert.Null(model.Value);

        // Re-set value
        model.Value = Constants.EventLevelError;

        // Act & Assert - Second change
        model.Category = FilterCategory.Source;
        Assert.Null(model.Value);
    }

    [Fact]
    public void Category_WhenChangedToSameCategory_ShouldStillClearValue()
    {
        // Arrange
        FilterData model = new() { Category = FilterCategory.Id, Value = Constants.FilterValue100 };

        // Act
        model.Category = FilterCategory.Id;

        // Assert
        Assert.Null(model.Value);
    }

    [Fact]
    public void Category_WhenChangedToSameCategory_ShouldStillClearValues()
    {
        // Arrange
        FilterData model = new() { Category = FilterCategory.Id, Values = [Constants.FilterValue100, Constants.FilterValue200] };

        // Act
        model.Category = FilterCategory.Id;

        // Assert
        Assert.Empty(model.Values);
    }

    [Fact]
    public void Category_WhenChangedWithBothValueAndValues_ShouldClearBoth()
    {
        // Arrange
        FilterData model = new()
        {
            Category = FilterCategory.Id,
            Value = Constants.FilterValue100,
            Values = [Constants.FilterValue200, Constants.FilterValue300]
        };

        // Act
        model.Category = FilterCategory.Description;

        // Assert
        Assert.Null(model.Value);
        Assert.Empty(model.Values);
    }

    [Theory]
    [InlineData(FilterCategory.Id)]
    [InlineData(FilterCategory.ActivityId)]
    [InlineData(FilterCategory.Level)]
    [InlineData(FilterCategory.Keywords)]
    [InlineData(FilterCategory.Source)]
    [InlineData(FilterCategory.TaskCategory)]
    [InlineData(FilterCategory.ProcessId)]
    [InlineData(FilterCategory.ThreadId)]
    [InlineData(FilterCategory.UserId)]
    [InlineData(FilterCategory.Description)]
    [InlineData(FilterCategory.Xml)]
    public void Category_WhenSetToAnyCategory_ShouldBeRetained(FilterCategory category)
    {
        // Arrange
        FilterData model = new();

        // Act
        model.Category = category;

        // Assert
        Assert.Equal(category, model.Category);
    }

    [Fact]
    public void Evaluator_WhenCategoryChanged_ShouldNotBeCleared()
    {
        // Arrange
        FilterData model = new()
        {
            Category = FilterCategory.Id,
            Evaluator = FilterEvaluator.NotEqual,
            Value = Constants.FilterValue100
        };

        // Act
        model.Category = FilterCategory.Source;

        // Assert
        Assert.Equal(FilterEvaluator.NotEqual, model.Evaluator);
    }

    [Fact]
    public void Evaluator_WhenSet_ShouldRetainEvaluator()
    {
        // Arrange
        FilterData model = new();

        // Act
        model.Evaluator = FilterEvaluator.Contains;

        // Assert
        Assert.Equal(FilterEvaluator.Contains, model.Evaluator);
    }

    [Theory]
    [InlineData(FilterEvaluator.Equals)]
    [InlineData(FilterEvaluator.Contains)]
    [InlineData(FilterEvaluator.NotEqual)]
    [InlineData(FilterEvaluator.NotContains)]
    [InlineData(FilterEvaluator.MultiSelect)]
    public void Evaluator_WhenSetToAnyEvaluator_ShouldBeRetained(FilterEvaluator evaluator)
    {
        // Arrange
        FilterData model = new();

        // Act
        model.Evaluator = evaluator;

        // Assert
        Assert.Equal(evaluator, model.Evaluator);
    }

    [Fact]
    public void NewInstance_ShouldHaveDefaultValues()
    {
        // Arrange & Act
        FilterData model = new();

        // Assert
        Assert.Equal(default, model.Category);
        Assert.Equal(default, model.Evaluator);
        Assert.Null(model.Value);
        Assert.NotNull(model.Values);
        Assert.Empty(model.Values);
    }

    [Fact]
    public void Value_WhenSet_ShouldRetainValue()
    {
        // Arrange
        FilterData model = new() { Category = FilterCategory.Id };

        // Act
        model.Value = Constants.FilterValue500;

        // Assert
        Assert.Equal(Constants.FilterValue500, model.Value);
    }

    [Fact]
    public void Values_WhenModifiedDirectly_ShouldReflectChanges()
    {
        // Arrange
        FilterData model = new() { Category = FilterCategory.Id };

        // Act
        model.Values.Add(Constants.FilterValue100);
        model.Values.Add(Constants.FilterValue200);

        // Assert
        Assert.Equal(2, model.Values.Count);
        Assert.Contains(Constants.FilterValue100, model.Values);
        Assert.Contains(Constants.FilterValue200, model.Values);
    }

    [Fact]
    public void Values_WhenSet_ShouldRetainValues()
    {
        // Arrange
        FilterData model = new() { Category = FilterCategory.Id };

        // Act
        model.Values = [Constants.FilterValue100, Constants.FilterValue200, Constants.FilterValue300];

        // Assert
        Assert.Equal(3, model.Values.Count);
        Assert.Contains(Constants.FilterValue100, model.Values);
        Assert.Contains(Constants.FilterValue200, model.Values);
        Assert.Contains(Constants.FilterValue300, model.Values);
    }
}
