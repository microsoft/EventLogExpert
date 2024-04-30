using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Tests.Models;

public sealed class FilterDataTests
{
    [Fact]
    public void FilterValue_WhenFilterTypeChanged_IsNull()
    {
        FilterData model = new() { Category = FilterCategory.Id, Value = "100" };

        model.Category = FilterCategory.Level;

        Assert.Null(model.Value);
    }

    [Fact]
    public void FilterValues_WhenFilterTypeChanged_IsEmpty()
    {
        FilterData model = new() { Category = FilterCategory.Id, Values = ["100", "1000"] };

        model.Category = FilterCategory.Level;

        Assert.Empty(model.Values);
    }
}
