// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Modal;

namespace EventLogExpert.UI.Tests.Modal;

public sealed class ModalBodyLayoutEnumStringificationTests
{
    [Fact]
    public void Content_ToStringLowerInvariant_ProducesLiteralContent()
    {
        Assert.Equal("content", nameof(ModalBodyLayout.Content).ToLowerInvariant());
    }

    [Fact]
    public void Flex_ToStringLowerInvariant_ProducesLiteralFlex()
    {
        Assert.Equal("flex", nameof(ModalBodyLayout.Flex).ToLowerInvariant());
    }
}
