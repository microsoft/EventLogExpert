// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Parsing;
using EventLogExpert.Filtering.Runtime;
using System.Diagnostics.CodeAnalysis;

namespace EventLogExpert.Filtering;

/// <summary>
///     Single source of truth for compiling a filter expression string into a <see cref="CompiledFilter" />. Public
///     facade that delegates to the hand-rolled <see cref="FilterParser" /> pipeline (tokenizer + parser + lowerer +
///     emitter); kept as the stable API surface for UI consumers (advanced row, saved filter loaders, filter service)
///     while the internal pipeline evolves.
/// </summary>
public static class FilterCompiler
{
    /// <summary>
    ///     Validates an expression without retaining the compiled result. Used by editor flows that only need a yes/no
    ///     answer (e.g. pre-save validation in the advanced row). Runs the full compile and discards the predicate so that
    ///     emitter-only validity checks (e.g. unsupported field/operator combinations) surface here, matching the pre-N3
    ///     behavior where <c>IsValid</c> delegated to <see cref="TryCompile" />.
    /// </summary>
    public static bool IsValid(string? expression, [NotNullWhen(false)] out string? error) =>
        TryCompile(expression, out _, out error);

    /// <summary>
    ///     Attempts to compile the supplied filter expression into a <see cref="CompiledFilter" />. Returns <c>true</c>
    ///     with <paramref name="compiled" /> populated on success; <c>false</c> with a diagnostic <paramref name="error" /> on
    ///     failure. Delegates to <see cref="FilterParser.TryCompile" />.
    /// </summary>
    public static bool TryCompile(
        string? expression,
        [NotNullWhen(true)] out CompiledFilter? compiled,
        [NotNullWhen(false)] out string? error) =>
        FilterParser.TryCompile(expression, out compiled, out error);
}
