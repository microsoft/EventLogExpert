// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Emit;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Lowering;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Filtering.Parsing;

internal static class FilterParser
{
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

        if (!Lowerer.TryLower(syntax!, out var filterNode, out error))
        {
            return false;
        }

        if (!Emitter.TryEmit(filterNode!, out compiled, out error))
        {
            return false;
        }

        error = null;

        return true;
    }

    public static bool TryCompileColumn(
        string? filterText,
        [NotNullWhen(true)] out ColumnCompiledFilter? compiled,
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

        if (!Lowerer.TryLower(syntax!, out var filterNode, out error))
        {
            return false;
        }

        if (!ColumnEmitter.TryEmit(filterNode!, out compiled, out error))
        {
            return false;
        }

        error = null;

        return true;
    }

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

