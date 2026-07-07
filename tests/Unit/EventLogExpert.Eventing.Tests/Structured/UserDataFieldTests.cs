// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Structured;

namespace EventLogExpert.Eventing.Tests.Structured;

/// <summary>
///     Coverage for the <see cref="UserDataField.ToFieldResult" /> seam, which projects a stored field into a
///     <see cref="StructuredFieldResult" /> inside the Eventing assembly so the filtering layer can evaluate a wildcard
///     glob match without reaching Eventing-internal value primitives.
/// </summary>
public sealed class UserDataFieldTests
{
    [Fact]
    public void ToFieldResult_PreservesSingleValueAndNoTruncation()
    {
        var field = new UserDataField("Foo", ["only"], IsTruncated: false);

        StructuredFieldResult result = field.ToFieldResult();

        Assert.False(result.IsTruncated);
        Assert.False(result.IsAbsent);
        Assert.Equal("only", result.Value.AsString());
    }

    [Fact]
    public void ToFieldResult_ProjectsValuesAndTruncationFlag()
    {
        var field = new UserDataField("Foo/Bar", ["a", "b"], IsTruncated: true);

        StructuredFieldResult result = field.ToFieldResult();

        Assert.True(result.IsTruncated);
        Assert.Equal(EventFieldValueKind.StringArray, result.Value.Kind);
        Assert.True(result.Value.TryGetStringArray(out string[]? values));
        Assert.Equal(["a", "b"], values);
    }
}
