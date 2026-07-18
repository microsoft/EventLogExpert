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
    [InlineData("errorCode", 0x800F081FL, "CBS_E_SOURCE_MISSING")]
    [InlineData("errorCode", 0x800F0922L, "CBS_E_INSTALLERS_FAILED")]
    [InlineData("errorCode", 0x800F0823L, "CBS_E_NEW_SERVICING_STACK_REQUIRED")]
    [InlineData("errorCode", 0x800F0816L, "CBS_E_DPX_JOB_STATE_SAVED")]
    [InlineData("errorCode", 0x80073712L, "ERROR_SXS_COMPONENT_STORE_CORRUPT")]
    [InlineData("errorCode", 0x80D05001L, "DO_E_HTTP_BLOCKSIZE_MISMATCH")]
    [InlineData("errorCode", 0x80246007L, "WU_E_DM_NOTDOWNLOADED")]
    [InlineData("errorCode", 0x8024200BL, "WU_E_UH_INSTALLERFAILURE")]
    public void TryDecodeLabel_KnownCode_ReturnsFriendlyLabel(string fieldName, long code, string expected) =>
        Assert.Equal(expected, EventDataValueDecoder.TryDecodeLabel(fieldName, code));

    [Fact]
    public void TryDecodeLabel_UndecodedField_ReturnsNull() =>
        Assert.Null(EventDataValueDecoder.TryDecodeLabel("TargetUserName", 3));

    [Theory]
    [InlineData("LogonType", 99)]
    [InlineData("TicketEncryptionType", 99)]
    [InlineData("errorCode", 0x80070005L)]
    public void TryDecodeLabel_UnrecognizedCode_ReturnsNull(string fieldName, long code) =>
        Assert.Null(EventDataValueDecoder.TryDecodeLabel(fieldName, code));
}
