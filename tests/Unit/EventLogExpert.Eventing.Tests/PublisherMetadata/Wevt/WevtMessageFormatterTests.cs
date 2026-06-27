// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.PublisherMetadata.Wevt;

namespace EventLogExpert.Eventing.Tests.PublisherMetadata.Wevt;

public sealed class WevtMessageFormatterTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("plain text", "plain text")]
    [InlineData("line1\r\nline2", "line1 line2")]
    [InlineData("line1\rline2", "line1 line2")]
    [InlineData("line1\nline2", "line1 line2")]
    [InlineData("\r\n\r\n", "  ")]
    [InlineData("a%nb", "a\r\nb")]
    [InlineData("a%tb", "a\tb")]
    [InlineData("a%bb", "a b")]
    [InlineData("a%rb", "a\rb")]
    [InlineData("a%.b", "a%.b")]
    [InlineData("%4%.", "%4%.")]
    [InlineData("Warning limit : %2%.", "Warning limit : %2%.")]
    [InlineData("100%%", "100%%")]
    [InlineData("%%1234", "%%1234")]
    [InlineData("a%0b", "a")]
    [InlineData("%0abc", "")]
    [InlineData("user %1 did %2", "user %1 did %2")]
    [InlineData("%1!S!", "%1")]
    [InlineData("status=%2!S!", "status=%2")]
    [InlineData("%1!S!:%2!d! Status: %3!S!", "%1:%2!d! Status: %3!S!")]
    [InlineData("%!LEVEL!] x %2!S!", "%!LEVEL!] x %2")]
    [InlineData("%10!S!", "%10")]
    [InlineData("%1!S", "%1!S")]
    [InlineData("a%1 b %2!S!", "a%1 b %2!S!")]
    [InlineData("%1!d!", "%1!d!")]
    [InlineData("%2!I64x! x %3!S!", "%2!I64x! x %3!S!")]
    [InlineData("%1!p! %2!S!", "%1!p! %2!S!")]
    [InlineData("%1!s!", "%1")]
    [InlineData("%1!!", "%1!!")]
    [InlineData("%%1 then %2!S!", "%%1 then %2")]
    [InlineData("%1!", "%1!")]
    [InlineData("%100", "%100")]
    [InlineData("%!PID!", "%!PID!")]
    [InlineData("%N%T%B%R", "%N%T%B%R")]
    [InlineData("50%", "50%")]
    [InlineData("a%nb\r\nc", "a\r\nb c")]
    public void Format_AppliesGrammarRule(string raw, string expected) =>
        Assert.Equal(expected, WevtMessageFormatter.Format(raw));

    [Theory]
    [InlineData("Account name is required.")]
    [InlineData("emoji \U0001F600 here")]
    public void Format_EscapeFreeText_ReturnsSameInstance(string raw) =>
        Assert.Same(raw, WevtMessageFormatter.Format(raw));

    [Fact]
    public void Format_SurrogatePairWithEscape_PreservesSurrogatePair() =>
        Assert.Equal("\U0001F600\r\nx", WevtMessageFormatter.Format("\U0001F600%nx"));
}
