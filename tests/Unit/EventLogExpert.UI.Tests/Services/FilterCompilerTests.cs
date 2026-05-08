// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Services;
using EventLogExpert.UI.Tests.TestUtils;
using EventLogExpert.UI.Tests.TestUtils.Constants;

namespace EventLogExpert.UI.Tests.Services;

public sealed class FilterCompilerTests
{
    [Fact]
    public void IsValid_WhenExpressionIsInvalid_ShouldReturnFalseWithError()
    {
        var valid = FilterCompiler.IsValid(Constants.FilterInvalidProperty, out var error);

        Assert.False(valid);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void IsValid_WhenExpressionIsValid_ShouldReturnTrue() =>
        Assert.True(FilterCompiler.IsValid(Constants.FilterIdEquals100, out _));

    [Fact]
    public void TryCompile_WhenExpressionDoesNotReferenceXml_ShouldNotRequireXml()
    {
        var success = FilterCompiler.TryCompile(Constants.FilterIdEquals100, out var compiled, out _);

        Assert.True(success);
        Assert.NotNull(compiled);
        Assert.False(compiled.RequiresXml);
    }

    [Fact]
    public void TryCompile_WhenExpressionIsInvalid_ShouldReturnFalseWithError()
    {
        var success = FilterCompiler.TryCompile(Constants.FilterInvalidProperty, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryCompile_WhenExpressionIsNullOrWhitespace_ShouldReturnFalse(string? expression)
    {
        var success = FilterCompiler.TryCompile(expression, out var compiled, out var error);

        Assert.False(success);
        Assert.Null(compiled);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void TryCompile_WhenExpressionIsValid_ShouldReturnCompiledFilter()
    {
        var success = FilterCompiler.TryCompile(Constants.FilterIdEquals100, out var compiled, out var error);

        Assert.True(success);
        Assert.NotNull(compiled);
        Assert.Null(error);

        var matching = EventUtils.CreateTestEvent(100);
        var nonMatching = EventUtils.CreateTestEvent(200);

        Assert.True(compiled.Predicate(matching));
        Assert.False(compiled.Predicate(nonMatching));
    }

    [Fact]
    public void TryCompile_WhenExpressionReferencesXml_ShouldReportRequiresXml()
    {
        var success = FilterCompiler.TryCompile(Constants.FilterXmlContainsData, out var compiled, out _);

        Assert.True(success);
        Assert.NotNull(compiled);
        Assert.True(compiled.RequiresXml);
    }
}
