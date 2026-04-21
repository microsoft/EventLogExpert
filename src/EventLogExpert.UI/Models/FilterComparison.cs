// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using System.Text.Json.Serialization;

namespace EventLogExpert.UI.Models;

public sealed record FilterComparison
{
    private string _value = string.Empty;

    public string Value
    {
        get => _value;
        set
        {
            var lambda = DynamicExpressionParser
                .ParseLambda<DisplayEventModel, bool>(new ParsingConfig { AllowEqualsAndToStringMethodsOnObject = true }, false, value);

            RequiresXml = ContainsXmlMemberAccess(lambda);
            Expression = lambda.Compile();

            _value = value;
        }
    }

    [JsonIgnore]
    public Func<DisplayEventModel, bool> Expression { get; private set; } = null!;

    /// <summary>
    /// True when the compiled filter expression references <see cref="DisplayEventModel.Xml"/>.
    /// Used by the eventing pipeline to decide whether logs must be opened with pre-rendered XML.
    /// </summary>
    [JsonIgnore]
    public bool RequiresXml { get; private set; }

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
            if (!Found
                && node.Member.Name == nameof(DisplayEventModel.Xml)
                && node.Member.DeclaringType == typeof(DisplayEventModel))
            {
                Found = true;
            }

            return base.VisitMember(node);
        }
    }
}
