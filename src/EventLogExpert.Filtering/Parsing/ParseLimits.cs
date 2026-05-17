// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Basic;

namespace EventLogExpert.Filtering.Parsing;

/// <summary>
///     Hard ceilings the parser enforces on incoming filter expressions. These exist so a hand-rolled parser cannot
///     be abused with deep parentheses, huge array literals, or pathologically long strings. Limits are deliberately
///     generous compared to anything <see cref="BasicFilterFormatter" /> emits or any plausible hand-written filter.
/// </summary>
internal static class ParseLimits
{
    public const int MaxArrayElements = 256;
    public const int MaxInputLength = 4096;
    public const int MaxParseDepth = 32;
    public const int MaxStringLiteralLength = 1024;
    public const int MaxTokens = 512;
}
