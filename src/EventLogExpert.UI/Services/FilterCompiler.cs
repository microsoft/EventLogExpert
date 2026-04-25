// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using EventLogExpert.UI.Models;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;

namespace EventLogExpert.UI.Services;

/// <summary>Single source of truth for compiling a Dynamic LINQ expression string into a <see cref="CompiledFilter" />.</summary>
public static class FilterCompiler
{
    private static readonly ParsingConfig s_parsingConfig =
        new() { AllowEqualsAndToStringMethodsOnObject = true };

    /// <summary>
    ///     Validates an expression without retaining the compiled result. Used by editor flows that only need a yes/no
    ///     answer (e.g. pre-save validation in the advanced row).
    /// </summary>
    public static bool IsValid(string? expression, [NotNullWhen(false)] out string? error) =>
        TryCompile(expression, out _, out error);

    /// <summary>
    ///     Attempts to compile the supplied Dynamic LINQ expression into a <see cref="CompiledFilter" />. Returns
    ///     <c>true</c> with <paramref name="compiled" /> populated on success; <c>false</c> with a diagnostic
    ///     <paramref name="error" /> on parse failure.
    /// </summary>
    public static bool TryCompile(
        string? expression,
        [NotNullWhen(true)] out CompiledFilter? compiled,
        [NotNullWhen(false)] out string? error)
    {
        compiled = null;
        error = null;

        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "Expression is empty.";

            return false;
        }

        try
        {
            var lambda = DynamicExpressionParser
                .ParseLambda<DisplayEventModel, bool>(s_parsingConfig, false, expression);

            compiled = new CompiledFilter(lambda.Compile(), ContainsXmlMemberAccess(lambda));

            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;

            return false;
        }
    }

    private static bool ContainsXmlMemberAccess(LambdaExpression lambda)
    {
        var visitor = new XmlMemberAccessVisitor();
        visitor.Visit(lambda);

        return visitor.Found;
    }

    private sealed class XmlMemberAccessVisitor : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (!Found &&
                node.Member.Name == nameof(DisplayEventModel.Xml) &&
                node.Member.DeclaringType == typeof(DisplayEventModel))
            {
                Found = true;
            }

            return base.VisitMember(node);
        }
    }
}
