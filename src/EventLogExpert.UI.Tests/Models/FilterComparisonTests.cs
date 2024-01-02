using EventLogExpert.UI.Models;
using System.Linq.Dynamic.Core.Exceptions;

namespace EventLogExpert.UI.Tests.Models;

public sealed class FilterComparisonTests
{
    [Fact]
    public void Expression_WhenValidValue_ShouldContainFunc()
    {
        FilterComparison model = new() { Value = "Id == 100" };

        Assert.NotNull(model.Expression);
    }

    [Fact]
    public void Value_WhenNotValid_ShouldThrow()
    {
        FilterComparison model = new();

        Assert.Throws<ParseException>(() => model.Value = "Id == invalid");
    }
}
