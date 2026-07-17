// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.TestUtils;
using EventLogExpert.Runtime.DetailsPane;

namespace EventLogExpert.Runtime.Tests.DetailsPane;

public sealed class EventFieldExplainerTests
{
    private const string SecurityAuditing = "Microsoft-Windows-Security-Auditing";

    [Fact]
    public void ErrorCode_NegativeHexInt32_DecodesToSymbol()
    {
        // The high-bit HexInt32 that the LogonType (TryGetWholeNumber) path rejects must decode through the HResult reader,
        // so the details pane shows the same curated symbol the histogram legend does.
        Assert.True(EventFieldExplainer.TryExplain("Microsoft-Windows-WindowsUpdateClient", 20, "errorCode", ValueOf(unchecked((int)0x800F081Fu)), out EventFieldExplanation explanation));
        Assert.Equal("CBS_E_SOURCE_MISSING", explanation.DecodedLabel);
    }

    [Fact]
    public void ErrorCode_UnknownCode_IsNotDecoded()
    {
        Assert.False(EventFieldExplainer.TryExplain("Microsoft-Windows-WindowsUpdateClient", 20, "errorCode", ValueOf(unchecked((int)0x80070005u)), out EventFieldExplanation explanation));
        Assert.Null(explanation.DecodedLabel);
    }

    [Fact]
    public void Explain_AmbiguousBareName_IsNotExplained()
    {
        Assert.False(EventFieldExplainer.TryExplain("Contoso-Provider", 1, "Status", ValueOf("0x0"), out EventFieldExplanation explanation));
        Assert.False(explanation.HasValue);
    }

    [Fact]
    public void Explain_DecoderAndGlossaryResolveIndependently()
    {
        Assert.True(EventFieldExplainer.TryExplain("Contoso-Provider", 1, "LogonType", ValueOf(3), out EventFieldExplanation explanation));

        Assert.Equal("Network", explanation.DecodedLabel);
        Assert.NotNull(explanation.Description);
    }

    [Fact]
    public void Explain_GenericField_AppliesAcrossProviders()
    {
        Assert.True(EventFieldExplainer.TryExplain("Contoso-Provider", 42, "IpAddress", ValueOf("10.0.0.1"), out EventFieldExplanation explanation));
        Assert.NotNull(explanation.Description);
    }

    [Fact]
    public void Explain_ProviderMismatch_DropsScopedDescription()
    {
        Assert.False(EventFieldExplainer.TryExplain("Contoso-Provider", 4624, "TargetUserName", ValueOf("SYSTEM"), out EventFieldExplanation explanation));
        Assert.Null(explanation.Description);
    }

    [Fact]
    public void Explain_ProviderScopedField_AppliesToAnyEvent()
    {
        Assert.True(EventFieldExplainer.TryExplain(SecurityAuditing, 9999, "AuthenticationPackageName", ValueOf("NTLM"), out EventFieldExplanation explanation));
        Assert.NotNull(explanation.Description);
    }

    [Fact]
    public void Explain_ScopedField_IsEventSpecific()
    {
        Assert.True(EventFieldExplainer.TryExplain(SecurityAuditing, 4624, "TargetUserName", ValueOf("SYSTEM"), out EventFieldExplanation logon));
        Assert.True(EventFieldExplainer.TryExplain(SecurityAuditing, 4625, "TargetUserName", ValueOf("SYSTEM"), out EventFieldExplanation failure));

        Assert.NotNull(logon.Description);
        Assert.NotNull(failure.Description);
        Assert.NotEqual(logon.Description, failure.Description);
    }

    [Fact]
    public void Explain_UnknownField_ReturnsFalse()
    {
        Assert.False(EventFieldExplainer.TryExplain("Contoso-Provider", 1, "SomeUnknownField", ValueOf("x"), out EventFieldExplanation explanation));
        Assert.False(explanation.HasValue);
    }

    [Theory]
    [InlineData(0, "System")]
    [InlineData(2, "Interactive")]
    [InlineData(3, "Network")]
    [InlineData(5, "Service")]
    [InlineData(10, "RemoteInteractive")]
    [InlineData(12, "CachedRemoteInteractive")]
    [InlineData(13, "CachedUnlock")]
    public void LogonType_DecodesKnownNumericValue(int logonType, string expected)
    {
        Assert.True(EventFieldExplainer.TryExplain("Contoso-Provider", 1, "LogonType", ValueOf(logonType), out EventFieldExplanation explanation));
        Assert.Equal(expected, explanation.DecodedLabel);
    }

    [Fact]
    public void LogonType_DecodesStringValue()
    {
        Assert.True(EventFieldExplainer.TryExplain("Contoso-Provider", 1, "LogonType", ValueOf("3"), out EventFieldExplanation explanation));
        Assert.Equal("Network", explanation.DecodedLabel);
    }

    [Fact]
    public void LogonType_DecodesUnsignedValue()
    {
        // A real Security-Auditing LogonType materializes as UInt32 -> EventFieldValueKind.UInt64, exercising the
        // TryGetUInt64 branch the int-typed cases above never reach.
        Assert.True(EventFieldExplainer.TryExplain("Contoso-Provider", 1, "LogonType", ValueOf(3u), out EventFieldExplanation explanation));
        Assert.Equal("Network", explanation.DecodedLabel);
    }

    [Fact]
    public void LogonType_NegativeInteger_IsNotDecoded()
    {
        Assert.True(EventFieldExplainer.TryExplain("Contoso-Provider", 1, "LogonType", ValueOf(-3), out EventFieldExplanation explanation));
        Assert.Null(explanation.DecodedLabel);
    }

    [Theory]
    [InlineData(" 3 ")]
    [InlineData("-3")]
    [InlineData("3.0")]
    [InlineData("three")]
    public void LogonType_NonCanonicalString_IsNotDecoded(string raw)
    {
        Assert.True(EventFieldExplainer.TryExplain("Contoso-Provider", 1, "LogonType", ValueOf(raw), out EventFieldExplanation explanation));
        Assert.Null(explanation.DecodedLabel);
    }

    [Fact]
    public void LogonType_UnknownValue_HasNoDecodedLabel()
    {
        Assert.True(EventFieldExplainer.TryExplain("Contoso-Provider", 1, "LogonType", ValueOf(99), out EventFieldExplanation explanation));

        Assert.Null(explanation.DecodedLabel);
        Assert.NotNull(explanation.Description);
    }

    private static EventFieldValue ValueOf(object? raw)
    {
        foreach (EventDataView.Field field in EventDataTestFactory.CreateEventWithData(("Field", raw)).EventData)
        {
            return field.Value;
        }

        throw new InvalidOperationException("Test factory produced no EventData field.");
    }
}
