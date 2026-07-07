// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Emit;

namespace EventLogExpert.Filtering.Tests.Emit;

/// <summary>
///     Unit coverage for the case-insensitive <c>*</c> glob matcher that backs EventData / UserData field-name
///     wildcards: anchored prefix/suffix, floating middles, doubled and edge <c>*</c>, and case-insensitivity.
/// </summary>
public sealed class WildcardMatcherTests
{
    [Theory]
    [InlineData("*", "anything", true)]
    [InlineData("*", "", true)]
    [InlineData("*cert*", "Certificate", true)]
    [InlineData("*cert*", "IssuerCertName", true)]
    [InlineData("*cert*", "CERTIFICATE", true)] // case-insensitive
    [InlineData("*cert*", "Subject", false)]
    [InlineData("cert*", "Certificate", true)] // anchored prefix
    [InlineData("cert*", "MyCertificate", false)]
    [InlineData("*name", "SubjectName", true)] // anchored suffix
    [InlineData("*name", "NameValue", false)]
    [InlineData("a*b*c", "axbyc", true)]
    [InlineData("a*b*c", "abc", true)]
    [InlineData("a*b*c", "ac", false)] // missing middle
    [InlineData("a**b", "axb", true)] // doubled star
    [InlineData("a*a", "a", false)] // prefix and suffix cannot overlap
    [InlineData("a*a", "aa", true)]
    [InlineData("aa*b*aa", "aaa", false)] // prefix + suffix overlap with a middle segment (start > end guard)
    [InlineData("X509*/@subjectName", "X509Objects/Certificate/@subjectName", true)] // path + attribute glob
    [InlineData("*/@*Name", "X509Objects/Certificate/@subjectName", true)]
    public void Compile_Matches(string pattern, string candidate, bool expected) =>
        Assert.Equal(expected, WildcardMatcher.Compile(pattern)(candidate));

    [Theory]
    [InlineData("*cert*", true)]
    [InlineData("cert", false)]
    [InlineData("", false)]
    public void ContainsWildcard(string pattern, bool expected) =>
        Assert.Equal(expected, WildcardMatcher.ContainsWildcard(pattern));
}
