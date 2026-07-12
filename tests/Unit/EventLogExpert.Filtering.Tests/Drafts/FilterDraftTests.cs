// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Compilation;
using EventLogExpert.Filtering.Drafts;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Filtering.TestUtils;
using EventLogExpert.Filtering.TestUtils.Constants;

namespace EventLogExpert.Filtering.Tests.Drafts;

public sealed class FilterDraftModelTests
{
    [Fact]
    public void FromSavedFilter_AdvancedModeWithStaleBasicFilter_DoesNotHydrateStructure()
    {
        // Arrange
        var staleBasic = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = FilterTestConstants.FilterValue100
            },
            []);

        var original = new SavedFilter
        {
            ComparisonText = FilterTestConstants.FilterIdEquals100,
            Compiled = FilterCompiler.TryCompile(FilterTestConstants.FilterIdEquals100, out var compiled, out _) ? compiled : null,
            BasicFilter = staleBasic,
            Mode = FilterMode.Advanced,
            IsEnabled = true
        };

        // Act
        var draft = FilterDraft.FromSavedFilter(original);

        // Assert
        Assert.Equal(FilterMode.Advanced, draft.Mode);
        Assert.Null(draft.Comparison.Value);
        Assert.Empty(draft.Predicates);
    }

    [Fact]
    public void FromSavedFilter_ContainsManyBlobWithEmpty_HydratesWithoutEmptyValue()
    {
        var basicFilter = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Source,
                Operator = ComparisonOperator.Contains,
                MatchMode = MatchMode.Many,
                Values = ["mshta.exe", ""]
            },
            []);

        var original = FilterBuilder.CreateTestFilter(basicFilter: basicFilter);

        var draft = FilterDraft.FromSavedFilter(original);

        // Reopening a legacy blank-carrying Basic filter shows a clean multiselect (Contains empty stripped on hydrate).
        Assert.Equal<IEnumerable<string>>(["mshta.exe"], draft.Comparison.Values);
    }

    [Fact]
    public void FromSavedFilter_DeepCopiesValuesList_SoEditorMutationDoesNotAffectModel()
    {
        // Arrange
        var basicFilter = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Many,
                Values = [FilterTestConstants.FilterValue100, FilterTestConstants.FilterValue1000]
            },
            []);

        var original = FilterBuilder.CreateTestFilter(basicFilter: basicFilter);

        // Act
        var draft = FilterDraft.FromSavedFilter(original);

        draft.Comparison.Values.Clear();
        draft.Comparison.Values.Add(FilterTestConstants.FilterValue500);

        // Assert
        Assert.NotNull(original.BasicFilter);
        Assert.Equal(2, original.BasicFilter.Comparison.Values.Count);
        Assert.Equal(FilterTestConstants.FilterValue100, original.BasicFilter.Comparison.Values[0]);
    }

    [Fact]
    public void FromSavedFilter_HydratesComparisonAndSubFiltersFromBasicFilter()
    {
        // Arrange
        var basicFilter = new BasicFilter(
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = FilterTestConstants.FilterValue100,
                Values = [FilterTestConstants.FilterValue100, FilterTestConstants.FilterValue1000]
            },
            [
                new FilterPredicate(
                    new FilterComparison
                    {
                        Property = EventProperty.Level,
                        Operator = ComparisonOperator.Equals,
                        MatchMode = MatchMode.Single,
                        Value = "Error"
                    },
                    true)
            ]);

        var original = FilterBuilder.CreateTestFilter(basicFilter: basicFilter);

        // Act
        var draft = FilterDraft.FromSavedFilter(original);

        // Assert
        Assert.Equal(EventProperty.Id, draft.Comparison.Property);
        Assert.Equal(FilterTestConstants.FilterValue100, draft.Comparison.Value);
        Assert.Equal(2, draft.Comparison.Values.Count);

        Assert.Single(draft.Predicates);
        Assert.True(draft.Predicates[0].JoinWithAny);
        Assert.Equal(EventProperty.Level, draft.Predicates[0].Comparison.Property);
        Assert.Equal("Error", draft.Predicates[0].Comparison.Value);
    }

    [Fact]
    public void FromSavedFilter_PreservesId()
    {
        // Arrange
        var original = FilterBuilder.CreateTestFilter();

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
            new FilterComparison
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = FilterTestConstants.FilterValue100
            },
            []);

        var original = FilterBuilder.CreateTestFilter(
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
        Assert.Equal(FilterTestConstants.FilterIdEquals100, draft.ComparisonText);
    }

    [Fact]
    public void FromSavedFilter_WhenNoBasicFilter_LeavesComparisonAndSubFiltersEmpty()
    {
        // Arrange
        var original = new SavedFilter
        {
            ComparisonText = FilterTestConstants.FilterIdEquals100,
            Compiled = FilterCompiler.TryCompile(FilterTestConstants.FilterIdEquals100, out var compiled, out _) ? compiled : null,
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
        Assert.Empty(draft.Predicates);
        Assert.Equal("Id == 100", draft.ComparisonText);
    }

    [Fact]
    public void ToBasicFilter_DoesNotShareValuesListWithDraft()
    {
        // Arrange
        var draft = FilterBuilder.CreateTestFilterDraft(
            comparison: new FilterComparisonDraft
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
        var draft = FilterBuilder.CreateTestFilterDraft(
            comparison: new FilterComparisonDraft
            {
                Property = EventProperty.Id,
                Operator = ComparisonOperator.Equals,
                MatchMode = MatchMode.Single,
                Value = FilterTestConstants.FilterValue100
            },
            predicates:
            [
                new FilterPredicateDraft
                {
                    Comparison = new FilterComparisonDraft
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
        Assert.Equal(FilterTestConstants.FilterValue100, source.Comparison.Value);
        Assert.Single(source.Predicates);
        Assert.True(source.Predicates[0].JoinWithAny);
        Assert.Equal(EventProperty.Level, source.Predicates[0].Comparison.Property);
        Assert.Equal("Error", source.Predicates[0].Comparison.Value);
    }

    [Fact]
    public void TryBuildSavedFilter_ContainsManyWithEmpty_NormalizesTextAndBlob()
    {
        var draft = new FilterDraft
        {
            Mode = FilterMode.Basic,
            Comparison =
            {
                Property = EventProperty.Source,
                Operator = ComparisonOperator.Contains,
                MatchMode = MatchMode.Many,
                Values = ["mshta.exe", "", "wscript.exe"]
            }
        };

        Assert.True(draft.TryBuildSavedFilter(out var saved, out _));

        // Both the compiled text AND the stored Basic model must be free of the degenerate empty value (no drift).
        Assert.DoesNotContain("\"\"", saved.ComparisonText);
        Assert.NotNull(saved.BasicFilter);
        Assert.Equal<IEnumerable<string>>(["mshta.exe", "wscript.exe"], saved.BasicFilter!.Comparison.Values);
    }
}
