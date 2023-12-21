using EventLogExpert.UI.Models;
using System.Linq.Dynamic.Core.Exceptions;

namespace EventLogExpert.UI.Tests.Models;

public sealed class AdvancedFilterModelTests
{
    [Fact]
    public void Comparison_WhenValid_ShouldContainFunc()
    {
        AdvancedFilterModel model = new() { ComparisonString = "Id == 100" };

        Assert.NotNull(model.Comparison);
    }

    [Fact]
    public void ComparisonString_WhenNotValid_ShouldThrow()
    {
        AdvancedFilterModel model = new();

        Assert.Throws<ParseException>(() => model.ComparisonString = "Id == invalid");
    }
}
