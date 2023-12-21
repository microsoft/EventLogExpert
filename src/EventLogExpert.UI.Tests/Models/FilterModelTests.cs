using System.Linq.Dynamic.Core.Exceptions;
using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Unit.Models;

public sealed class FilterModelTests
{
    [Fact]
    public void Comparison_WhenValid_ShouldContainFunc()
    {
        FilterModel model = new() { ComparisonString = "Id == 100" };

        Assert.NotNull(model.Comparison);
    }

    [Fact]
    public void ComparisonString_WhenNotValid_ShouldThrow()
    {
        FilterModel model = new();

        Assert.Throws<ParseException>(() => model.ComparisonString = "Id == invalid");
    }

    [Fact]
    public void FilterValue_WhenFilterTypeChanged_IsNull()
    {
        FilterModel model = new() { FilterType = FilterType.Id, FilterValue = "100" };

        model.FilterType = FilterType.Level;

        Assert.Null(model.FilterValue);
    }

    [Fact]
    public void FilterValues_WhenFilterTypeChanged_IsEmpty()
    {
        FilterModel model = new() { FilterType = FilterType.Id, FilterValues = ["100", "1000"] };

        model.FilterType = FilterType.Level;

        Assert.Empty(model.FilterValues);
    }
}
