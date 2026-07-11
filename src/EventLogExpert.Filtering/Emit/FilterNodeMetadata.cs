// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Lowering;

namespace EventLogExpert.Filtering.Emit;

/// <summary>
///     Source-agnostic structural queries over the lowered <see cref="FilterNode" /> filter graph, shared by the row
///     <see cref="Emitter" /> and the column <see cref="ColumnEmitter" />: the <c>RequiresXml</c> flag and the And/Or
///     chain flatteners.
/// </summary>
internal static class FilterNodeMetadata
{
    public static bool ContainsXmlReference(FilterNode node) =>
        node switch
        {
            AndNode and => ContainsXmlReference(and.Left) || ContainsXmlReference(and.Right),
            OrNode or => ContainsXmlReference(or.Left) || ContainsXmlReference(or.Right),
            NotNode not => ContainsXmlReference(not.Operand),
            ComparisonNode cmp => cmp.Field == ResolvedEventField.Xml,
            ContainsNode cn => cn.Field == ResolvedEventField.Xml,
            MultiEqualsNode mn => mn.Field == ResolvedEventField.Xml,
            MultiContainsNode mcn => mcn.Field == ResolvedEventField.Xml,
            _ => false
        };

    public static List<FilterNode> FlattenAndChain(FilterNode node)
    {
        var list = new List<FilterNode>();

        Flatten(node, list);

        return list;

        static void Flatten(FilterNode current, List<FilterNode> accumulator)
        {
            if (current is AndNode and)
            {
                Flatten(and.Left, accumulator);
                Flatten(and.Right, accumulator);
            }
            else
            {
                accumulator.Add(current);
            }
        }
    }

    public static List<FilterNode> FlattenOrChain(FilterNode node)
    {
        var list = new List<FilterNode>();

        Flatten(node, list);

        return list;

        static void Flatten(FilterNode current, List<FilterNode> accumulator)
        {
            if (current is OrNode or)
            {
                Flatten(or.Left, accumulator);
                Flatten(or.Right, accumulator);
            }
            else
            {
                accumulator.Add(current);
            }
        }
    }
}
