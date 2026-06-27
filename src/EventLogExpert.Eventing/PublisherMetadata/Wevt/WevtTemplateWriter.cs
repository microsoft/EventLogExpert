// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Globalization;
using System.Text;

namespace EventLogExpert.Eventing.PublisherMetadata.Wevt;

/// <summary>
///     Writes the manifest <c>&lt;template&gt;</c> XML for the parsed WEVT_TEMPLATE node tree: flat
///     <c>&lt;data&gt;</c> leaves and <c>&lt;struct&gt;</c> wrappers around their member leaves. Each leaf carries only
///     name / inType / outType / length / count; the <c>map</c> attribute is injected separately by
///     <see cref="ProviderDetailsFactory.InjectMapAttribute" /> so a map is never written twice. The whole template fails
///     closed (a null return) when any field is unrepresentable - an unknown inType or outType byte, a length that is
///     neither the pinned field-name reference nor a fixed length on a variable-length type, or a count reference that is
///     out of range or points at a struct - so the reader never emits a guessed or partial template.
/// </summary>
internal static class WevtTemplateWriter
{
    private const byte AnsiStringInType = 0x02;

    private const byte BinaryInType = 0x0e;

    private const uint FixedCountArrayFlag = 0x8;

    private const uint FixedLengthFlag = 0x2;

    private const uint LengthFieldReferenceFlag = 0x4;

    private const string TemplateNamespace = "http://schemas.microsoft.com/win/2004/08/events";

    private const byte UnicodeStringInType = 0x01;

    private const uint VariableCountArrayFlag = 0x10;

    /// <summary>
    ///     Escapes a value for an XML attribute exactly as <see cref="Write" /> writes field names, so the factory's map
    ///     injection searches for the same escaped name it emitted (otherwise injection silently misses).
    /// </summary>
    internal static string EscapeXmlAttribute(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");

    internal static string? Write(
        IReadOnlyList<WevtTemplateNode> nodes,
        IReadOnlyList<WevtRawDescriptor> descriptors)
    {
        StringBuilder builder = new();
        builder.Append("<template xmlns=\"").Append(TemplateNamespace).Append("\">");

        if (!TryAppendNodes(builder, nodes, descriptors))
        {
            return null;
        }

        builder.Append("</template>");

        return builder.ToString();
    }

    private static bool IsFixedLengthBearingInType(byte inType) =>
        inType is UnicodeStringInType or AnsiStringInType or BinaryInType;

    private static bool TryAppendLeaf(
        StringBuilder builder,
        WevtLeafNode leaf,
        IReadOnlyList<WevtRawDescriptor> descriptors)
    {
        if (!WevtTypeNames.TryGetInType(leaf.InType, out string? inType) ||
            !WevtTypeNames.TryGetOutType(leaf.InType, leaf.OutType, out string? outType) ||
            !TryResolveLength(leaf, descriptors, out string? lengthValue) ||
            !TryResolveCount(leaf.Flags, leaf.ArrayCount, leaf.InType, descriptors, out string? countValue))
        {
            return false;
        }

        builder.Append("<data name=\"").Append(EscapeXmlAttribute(leaf.Name)).Append('"');
        builder.Append(" inType=\"").Append(EscapeXmlAttribute(inType)).Append('"');

        // The live API always emits an outType, even when it equals the inType's winmeta default, so it is never omitted.
        builder.Append(" outType=\"").Append(EscapeXmlAttribute(outType)).Append('"');

        if (lengthValue is not null)
        {
            builder.Append(" length=\"").Append(EscapeXmlAttribute(lengthValue)).Append('"');
        }

        if (countValue is not null)
        {
            builder.Append(" count=\"").Append(EscapeXmlAttribute(countValue)).Append('"');
        }

        builder.Append("/>");

        return true;
    }

    private static bool TryAppendNodes(
        StringBuilder builder,
        IReadOnlyList<WevtTemplateNode> nodes,
        IReadOnlyList<WevtRawDescriptor> descriptors)
    {
        foreach (WevtTemplateNode node in nodes)
        {
            bool appended = node switch
            {
                WevtLeafNode leaf => TryAppendLeaf(builder, leaf, descriptors),
                WevtStructNode structNode => TryAppendStruct(builder, structNode, descriptors),
                _ => false
            };

            if (!appended) { return false; }
        }

        return true;
    }

    private static bool TryAppendStruct(
        StringBuilder builder,
        WevtStructNode structNode,
        IReadOnlyList<WevtRawDescriptor> descriptors)
    {
        if (!TryResolveCount(structNode.Flags, structNode.ArrayCount, inType: 0, descriptors, out string? countValue))
        {
            return false;
        }

        builder.Append("<struct name=\"").Append(EscapeXmlAttribute(structNode.Name)).Append('"');

        if (countValue is not null)
        {
            builder.Append(" count=\"").Append(EscapeXmlAttribute(countValue)).Append('"');
        }

        builder.Append('>');

        foreach (WevtLeafNode member in structNode.Members)
        {
            if (!TryAppendLeaf(builder, member, descriptors))
            {
                return false;
            }
        }

        builder.Append("</struct>");

        return true;
    }

    private static bool TryResolveCount(
        uint flags,
        ushort arrayCount,
        byte inType,
        IReadOnlyList<WevtRawDescriptor> descriptors,
        out string? countValue)
    {
        countValue = null;

        if ((flags & VariableCountArrayFlag) != 0)
        {
            if (arrayCount >= descriptors.Count || descriptors[arrayCount].IsStruct)
            {
                return false;
            }

            string referencedName = descriptors[arrayCount].Name;

            if (referencedName.Length == 0) { return false; }

            countValue = referencedName;

            return true;
        }

        if ((flags & FixedCountArrayFlag) != 0)
        {
            countValue = arrayCount.ToString(CultureInfo.InvariantCulture);

            return true;
        }

        if ((inType & WevtTypeNames.ArrayFlag) == 0) { return true; }

        countValue = (arrayCount == 0 ? 1 : arrayCount).ToString(CultureInfo.InvariantCulture);

        return true;
    }

    private static bool TryResolveLength(
        WevtLeafNode leaf,
        IReadOnlyList<WevtRawDescriptor> descriptors,
        out string? lengthValue)
    {
        lengthValue = null;

        if ((leaf.Flags & LengthFieldReferenceFlag) != 0)
        {
            if (leaf.Length >= descriptors.Count || descriptors[leaf.Length].IsStruct)
            {
                return false;
            }

            string referencedName = descriptors[leaf.Length].Name;

            if (referencedName.Length == 0) { return false; }

            lengthValue = referencedName;

            return true;
        }

        if ((leaf.Flags & FixedLengthFlag) == 0) { return true; }

        if (leaf.Length == 0 || !IsFixedLengthBearingInType(leaf.InType))
        {
            return false;
        }

        lengthValue = leaf.Length.ToString(CultureInfo.InvariantCulture);

        return true;
    }
}
