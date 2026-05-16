// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.UI.Filter;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;

namespace EventLogExpert.UI.Tests.Filter;

public sealed class FilterDraftModelTests
{
    [Fact]
    public void FromSavedFilter_AdvancedModeWithStaleBasicFilter_DoesNotHydrateStructure()
    {
        // Arrange
        var staleBasic = new BasicFilter(
            new BasicFilterCondition
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = Constants.FilterValue100
            },
            []);

        var original = new SavedFilter
        {
            ComparisonText = Constants.FilterIdEquals100,
            Compiled = FilterCompiler.TryCompile(Constants.FilterIdEquals100, out var compiled, out _) ? compiled : null,
            BasicFilter = staleBasic,
            Mode = FilterMode.Advanced,
            IsEnabled = true
        };

        // Act
        var draft = FilterDraft.FromSavedFilter(original);

        // Assert
        Assert.Equal(FilterMode.Advanced, draft.Mode);
        Assert.Null(draft.Comparison.Value);
        Assert.Empty(draft.SubFilters);
    }

    [Fact]
    public void FromSavedFilter_DeepCopiesValuesList_SoEditorMutationDoesNotAffectModel()
    {
        // Arrange
        var basicFilter = new BasicFilter(
            new BasicFilterCondition
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Many,
                Values = [Constants.FilterValue100, Constants.FilterValue1000]
            },
            []);

        var original = FilterUtils.CreateTestFilter(basicFilter: basicFilter);

        // Act
        var draft = FilterDraft.FromSavedFilter(original);

        draft.Comparison.Values.Clear();
        draft.Comparison.Values.Add(Constants.FilterValue500);

        // Assert
        Assert.NotNull(original.BasicFilter);
        Assert.Equal(2, original.BasicFilter.Comparison.Values.Count);
        Assert.Equal(Constants.FilterValue100, original.BasicFilter.Comparison.Values[0]);
    }

    [Fact]
    public void FromSavedFilter_HydratesComparisonAndSubFiltersFromBasicFilter()
    {
        // Arrange
        var basicFilter = new BasicFilter(
            new BasicFilterCondition
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = Constants.FilterValue100,
                Values = [Constants.FilterValue100, Constants.FilterValue1000]
            },
            [
                new SubFilter(
                    new BasicFilterCondition
                    {
                        Property = EventProperty.Level,
                        Operator = ComparisonOperator.Equals,
                        MatchMode = MatchMode.Single,
                        Value = "Error"
                    },
                    true)
            ]);

        var original = FilterUtils.CreateTestFilter(basicFilter: basicFilter);

        // Act
        var draft = FilterDraft.FromSavedFilter(original);

        // Assert
        Assert.Equal(EventProperty.Id, draft.Comparison.Property);
        Assert.Equal(Constants.FilterValue100, draft.Comparison.Value);
        Assert.Equal(2, draft.Comparison.Values.Count);

        Assert.Single(draft.SubFilters);
        Assert.True(draft.SubFilters[0].JoinWithAny);
        Assert.Equal(EventProperty.Level, draft.SubFilters[0].Condition.Property);
        Assert.Equal("Error", draft.SubFilters[0].Condition.Value);
    }

    [Fact]
    public void FromSavedFilter_PreservesId()
    {
        // Arrange
        var original = FilterUtils.CreateTestFilter();

        // Act
        var draft = FilterDraft.FromSavedFilter(original);

        // Assert
        Assert.Equal(original.Id, draft.Id);
    }

    [Fact]
    public void FromSavedFilter_PreservesScalarFields()
    {
        // Arrange
        var basicFilter = new BasicFilter(
            new BasicFilterCondition
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = Constants.FilterValue100
            },
            []);

        var original = FilterUtils.CreateTestFilter(
            color: HighlightColor.Blue,
            basicFilter: basicFilter,
            isEnabled: true,
            isExcluded: true);

        // Act
        var draft = FilterDraft.FromSavedFilter(original);

        // Assert
        Assert.Equal(HighlightColor.Blue, draft.Color);
        Assert.Equal(FilterMode.Basic, draft.Mode);
        Assert.True(draft.IsEnabled);
        Assert.True(draft.IsExcluded);
        Assert.Equal(Constants.FilterIdEquals100, draft.ComparisonText);
    }

    [Fact]
    public void FromSavedFilter_WhenNoBasicFilter_LeavesComparisonAndSubFiltersEmpty()
    {
        // Arrange
        var original = new SavedFilter
        {
            ComparisonText = Constants.FilterIdEquals100,
            Compiled = FilterCompiler.TryCompile(Constants.FilterIdEquals100, out var compiled, out _) ? compiled : null,
            BasicFilter = null,
            Mode = FilterMode.Advanced,
            IsEnabled = true
        };

        // Act
        var draft = FilterDraft.FromSavedFilter(original);

        // Assert
        Assert.Equal(FilterMode.Advanced, draft.Mode);
        Assert.Equal(EventProperty.Id, draft.Comparison.Property);
        Assert.Null(draft.Comparison.Value);
        Assert.Empty(draft.Comparison.Values);
        Assert.Empty(draft.SubFilters);
        Assert.Equal("Id == 100", draft.ComparisonText);
    }

    [Fact]
    public void ToBasicFilter_DoesNotShareValuesListWithDraft()
    {
        // Arrange
        var draft = FilterUtils.CreateTestFilterDraft(
            comparison: new FilterConditionDraft
            {
                Property = EventProperty.Level,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Many,
                Values = ["Error"]
            });

        // Act
        var source = draft.ToBasicFilter();

        draft.Comparison.Values.Add("Warning");

        // Assert
        Assert.Single(source.Comparison.Values);
        Assert.Equal("Error", source.Comparison.Values[0]);
    }

    [Fact]
    public void ToBasicFilter_ProducesImmutableSourceMatchingEditorState()
    {
        // Arrange
        var draft = FilterUtils.CreateTestFilterDraft(
            comparison: new FilterConditionDraft
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = Constants.FilterValue100
            },
            subFilters:
            [
                new SubFilterDraft
                {
                    Condition = new FilterConditionDraft
                    {
                        Property = EventProperty.Level,
                        Operator = ComparisonOperator.Equals,
                        MatchMode = MatchMode.Single,
                        Value = "Error"
                    },
                    JoinWithAny = true
                }
            ]);

        // Act
        var source = draft.ToBasicFilter();

        // Assert
        Assert.Equal(EventProperty.Id, source.Comparison.Property);
        Assert.Equal(Constants.FilterValue100, source.Comparison.Value);
        Assert.Single(source.SubFilters);
        Assert.True(source.SubFilters[0].JoinWithAny);
        Assert.Equal(EventProperty.Level, source.SubFilters[0].Comparison.Property);
        Assert.Equal("Error", source.SubFilters[0].Comparison.Value);
    }
}
