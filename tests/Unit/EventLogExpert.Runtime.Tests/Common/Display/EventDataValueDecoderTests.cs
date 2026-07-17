// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Display;

namespace EventLogExpert.Runtime.Tests.Common.Display;

public sealed class EventDataValueDecoderTests
{
    [Fact]
    public void TryDecodeLabel_FieldNameIsCaseInsensitive() =>
        Assert.Equal("Network", EventDataValueDecoder.TryDecodeLabel("logontype", 3));

    [Theory]
    [InlineData("LogonType", 0, "System")]
    [InlineData("LogonType", 3, "Network")]
    [InlineData("LogonType", 10, "RemoteInteractive")]
    [InlineData("TicketEncryptionType", 17, "AES128")]
    [InlineData("TicketEncryptionType", 18, "AES256")]
    [InlineData("TicketEncryptionType", 23, "RC4")]
    public void TryDecodeLabel_KnownCode_ReturnsFriendlyLabel(string fieldName, long code, string expected) =>
        Assert.Equal(expected, EventDataValueDecoder.TryDecodeLabel(fieldName, code));

    [Fact]
    public void TryDecodeLabel_UndecodedField_ReturnsNull() =>
        Assert.Null(EventDataValueDecoder.TryDecodeLabel("TargetUserName", 3));

    [Theory]
    [InlineData("LogonType", 99)]
    [InlineData("TicketEncryptionType", 99)]
    public void TryDecodeLabel_UnrecognizedCode_ReturnsNull(string fieldName, long code) =>
        Assert.Null(EventDataValueDecoder.TryDecodeLabel(fieldName, code));
}
