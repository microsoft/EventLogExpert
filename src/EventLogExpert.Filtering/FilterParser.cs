// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Emit;
using EventLogExpert.Filtering.Lowering;
using EventLogExpert.Filtering.Parsing;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Filtering;

/// <summary>
///     Lightweight, allocation-conscious validation entry point for filter expression strings. Runs the full
///     tokenizer + parser + lowerer pipeline (per N-D5: identifier resolution and method-shape validation happen in the
///     lowerer, not just syntactic parsing) but skips closure emission. Intended for editor pre-flight (live keystroke
///     validation) and as the testable seam for the internal AST in the absence of <c>InternalsVisibleTo</c>.
/// </summary>
public static class FilterParser
{
    /// <summary>
    ///     Compiles <paramref name="filterText" /> to a <see cref="CompiledFilter" /> via the hand-rolled tokenizer +
    ///     parser + lowerer + emitter pipeline, with no LINQ-Expression compilation, no Reflection.Emit, and no Dynamic.Core
    ///     dependency on the per-event hot path. Returns <c>false</c> with a human-readable diagnostic in
    ///     <paramref name="error" /> on any failure. Never throws on bad input.
    /// </summary>
    public static bool TryCompile(
        string? filterText,
        [NotNullWhen(true)] out CompiledFilter? compiled,
        [NotNullWhen(false)] out string? error)
    {
        compiled = null;

        if (string.IsNullOrWhiteSpace(filterText))
        {
            error = "Expression is empty.";

            return false;
        }

        if (!Tokenizer.TryTokenize(filterText, out var tokens, out error))
        {
            return false;
        }

        if (!Parser.TryParse(tokens, out var syntax, out error))
        {
            return false;
        }

        if (!Lowerer.TryLower(syntax!, out var semantic, out error))
        {
            return false;
        }

        if (!Emitter.TryEmit(semantic!, out compiled, out error))
        {
            return false;
        }

        error = null;

        return true;
    }

    /// <summary>
    ///     Returns <c>true</c> when <paramref name="filterText" /> is a syntactically and semantically well-formed filter
    ///     expression in the closed Dynamic-LINQ subset this library supports. Returns <c>false</c> with a human-readable
    ///     diagnostic in <paramref name="error" /> otherwise. Never throws on bad input.
    /// </summary>
    public static bool TryValidate(string? filterText, [NotNullWhen(false)] out string? error)
    {
        if (string.IsNullOrWhiteSpace(filterText))
        {
            error = "Expression is empty.";

            return false;
        }

        if (!Tokenizer.TryTokenize(filterText, out var tokens, out error))
        {
            return false;
        }

        if (!Parser.TryParse(tokens, out var syntax, out error))
        {
            return false;
        }

        if (!Lowerer.TryLower(syntax!, out _, out error))
        {
            return false;
        }

        error = null;

        return true;
    }
}

