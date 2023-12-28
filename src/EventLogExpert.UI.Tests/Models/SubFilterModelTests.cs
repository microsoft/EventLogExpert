using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.UnitTests.Models;

public sealed class SubFilterModelTests
{
    [Fact]
    public void FilterValue_WhenFilterTypeChanged_IsNull()
    {
        SubFilterModel model = new() { FilterType = FilterType.Id, FilterValue = "100" };

        model.FilterType = FilterType.Level;

        Assert.Null(model.FilterValue);
    }

    [Fact]
    public void FilterValues_WhenFilterTypeChanged_IsEmpty()
    {
        SubFilterModel model = new() { FilterType = FilterType.Id, FilterValues = ["100", "1000"] };

        model.FilterType = FilterType.Level;

        Assert.Empty(model.FilterValues);
    }
}
