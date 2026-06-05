// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;

namespace EventLogExpert.Runtime.Tests.Alerts;

public sealed class InlineAlertRequestTests
{
    [Fact]
    public void PositionalCtor_BackwardCompatible_DoesNotRequireValidate()
    {
        var request = new InlineAlertRequest("T", "M", "OK", "Cancel", false, null);

        Assert.Equal("T", request.Title);
        Assert.Equal("M", request.Message);
        Assert.Null(request.Validate);
    }

    [Fact]
    public void Validate_DefaultsToNull()
    {
        var request = new InlineAlertRequest(
            Title: "T",
            Message: "M",
            AcceptLabel: "OK",
            CancelLabel: "Cancel",
            IsPrompt: false,
            PromptInitialValue: null);

        Assert.Null(request.Validate);
    }

    [Fact]
    public void Validate_SetViaInit_Persists()
    {
        Func<string, string?> validator = s => s.Length > 0 ? null : "empty";

        var request = new InlineAlertRequest(
            Title: "T",
            Message: "M",
            AcceptLabel: "OK",
            CancelLabel: "Cancel",
            IsPrompt: true,
            PromptInitialValue: "v")
        {
            Validate = validator,
        };

        Assert.Same(validator, request.Validate);
        Assert.Null(request.Validate!("hi"));
        Assert.Equal("empty", request.Validate(string.Empty));
    }
}
