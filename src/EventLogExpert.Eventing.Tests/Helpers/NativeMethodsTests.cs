// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;

namespace EventLogExpert.Eventing.Tests.Helpers;

public sealed class NativeMethodsTests
{
    [Theory]
    [InlineData("%1 parameter", true)]
    [InlineData("%9 parameter", true)]
    [InlineData("error %1 occurred", true)]
    [InlineData("code %10 detail", true)]
    [InlineData("code %99 detail", true)]
    [InlineData("%1!s! insert", true)]
    [InlineData("value: %s", true)]
    [InlineData("value: %S", true)]
    [InlineData("count: %d", true)]
    [InlineData("count: %i", true)]
    [InlineData("count: %u", true)]
    [InlineData("hex: %x", true)]
    [InlineData("hex: %X", true)]
    [InlineData("ptr: %p", true)]
    [InlineData("char: %c", true)]
    [InlineData("char: %C", true)]
    [InlineData("octal: %o", true)]
    [InlineData("float: %f", true)]
    [InlineData("float: %F", true)]
    [InlineData("sci: %e", true)]
    [InlineData("sci: %E", true)]
    [InlineData("gen: %g", true)]
    [InlineData("gen: %G", true)]
    [InlineData("100%% complete", false)]
    [InlineData("clean message", false)]
    [InlineData("100% done", false)]
    [InlineData("value %0", false)]
    [InlineData("trailing %", false)]
    [InlineData("", false)]
    [InlineData("%%s escaped", false)]
    [InlineData("a %% b %% c", false)]
    public void ContainsFormatInsert_DetectsExpectedPatterns(string input, bool expected) =>
        Assert.Equal(expected, NativeMethods.ContainsFormatInsert(input));
}
