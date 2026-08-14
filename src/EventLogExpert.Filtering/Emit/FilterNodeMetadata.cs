// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Lowering;

namespace EventLogExpert.Filtering.Emit;

/// <summary>
///     Source-agnostic structural queries over the lowered <see cref="FilterNode" /> filter graph, shared by the row
///     <see cref="Emitter" /> and the column <see cref="ColumnEmitter" />: the <c>RequiresXml</c> flag, the And/Or chain
///     flatteners, and the cheap-versus-XML condition partition.
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

    public static (List<FilterNode> CheapConditions, List<FilterNode> XmlConditions) PartitionAndChain(FilterNode node)
    {
        List<FilterNode> cheapConditions = [];
        List<FilterNode> xmlConditions = [];

        foreach (var condition in FlattenAndChain(node))
        {
            if (ContainsXmlReference(condition)) { xmlConditions.Add(condition); }
            else { cheapConditions.Add(condition); }
        }

        return (cheapConditions, xmlConditions);
    }
}
