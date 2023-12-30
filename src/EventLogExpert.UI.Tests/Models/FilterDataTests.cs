using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Tests.Models;

public sealed class FilterDataTests
{
    [Fact]
    public void FilterValue_WhenFilterTypeChanged_IsNull()
    {
        FilterData model = new() { Type = FilterType.Id, Value = "100" };

        model.Type = FilterType.Level;

        Assert.Null(model.Value);
    }

    [Fact]
    public void FilterValues_WhenFilterTypeChanged_IsEmpty()
    {
        FilterData model = new() { Type = FilterType.Id, Values = ["100", "1000"] };

        model.Type = FilterType.Level;

        Assert.Empty(model.Values);
    }
}
