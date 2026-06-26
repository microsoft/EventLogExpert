// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Globalization;
using System.Text;

namespace EventLogExpert.Eventing.PublisherMetadata.Wevt;

/// <summary>
///     Synthesizes a manifest <c>&lt;template&gt;</c> from flat WEVT_TEMPLATE item descriptors. The synthesized
///     template carries only name / inType / outType / length / count: the <c>map</c> attribute is injected separately by
///     <see cref="ProviderDetailsAssembler.InjectMapAttribute" /> so a map is never written twice. The whole template
///     fails closed (a null return) when any field is unrepresentable - an unknown inType or outType byte, a length that
///     is not the pinned field-name reference, or an out-of-range array count reference - so the reader never emits a
///     guessed or partial template.
/// </summary>
internal static class WevtTemplateSynthesizer
{
    private const uint FixedCountArrayFlag = 0x8;

    private const uint FixedLengthFlag = 0x2;

    private const uint LengthFieldReferenceFlag = 0x4;

    private const string TemplateNamespace = "http://schemas.microsoft.com/win/2004/08/events";

    private const uint VariableCountArrayFlag = 0x10;

    /// <summary>
    ///     Escapes a value for an XML attribute exactly as <see cref="Synthesize" /> writes field names, so the
    ///     assembler's map injection searches for the same escaped name it emitted (otherwise injection silently misses).
    /// </summary>
    internal static string EscapeXmlAttribute(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    internal static string? Synthesize(IReadOnlyList<WevtTemplateItem> items)
    {
        StringBuilder builder = new();
        builder.Append("<template xmlns=\"").Append(TemplateNamespace).Append("\">");

        foreach (WevtTemplateItem item in items)
        {
            if (!WevtTypeNames.TryGetInType(item.InType, out string? inType) ||
                !WevtTypeNames.TryGetOutType(item.InType, item.OutType, out string? outType) ||
                !TryResolveLengthReference(items, item, out string? lengthName) ||
                !TryResolveArrayCount(items, item, out string? countValue))
            {
                // An unknown inType / outType byte, a non-field-reference length, or an out-of-range array count is
                // unrepresentable here, so the whole template fails closed rather than emit a guessed or partial field.
                return null;
            }

            builder.Append("<data name=\"").Append(EscapeXmlAttribute(item.Name)).Append('"');
            builder.Append(" inType=\"").Append(EscapeXmlAttribute(inType)).Append('"');

            // The live API always emits an outType, even when it equals the inType's winmeta default, so it is never
            // omitted here.
            builder.Append(" outType=\"").Append(EscapeXmlAttribute(outType)).Append('"');

            if (lengthName is not null)
            {
                builder.Append(" length=\"").Append(EscapeXmlAttribute(lengthName)).Append('"');
            }

            if (countValue is not null)
            {
                builder.Append(" count=\"").Append(EscapeXmlAttribute(countValue)).Append('"');
            }

            builder.Append("/>");
        }

        builder.Append("</template>");

        return builder.ToString();
    }

    /// <summary>
    ///     Resolves the optional <c>count</c> attribute that marks an array field. A fixed-length array (flags 0x8) emits
    ///     count="&lt;literal element count&gt;" taken from count@12; a variable-length array (flags 0x10) emits the name of
    ///     the field that supplies the element count, where count@12 holds that field's 0-based index. The legacy EVT_VARIANT
    ///     array flag (inType 0x80, not observed in compiled manifests) is treated as a fixed-length array. Returns
    ///     <see langword="false" /> (fail closed) for an out-of-range or unnamed variable-array reference; returns
    ///     <see langword="true" /> with a null <paramref name="countValue" /> when the field is not an array.
    /// </summary>
    private static bool TryResolveArrayCount(
        IReadOnlyList<WevtTemplateItem> items,
        WevtTemplateItem item,
        out string? countValue)
    {
        countValue = null;

        if ((item.Flags & VariableCountArrayFlag) != 0)
        {
            if (item.Count >= items.Count) { return false; }

            string referencedName = items[item.Count].Name;

            if (referencedName.Length == 0) { return false; }

            countValue = referencedName;

            return true;
        }

        if ((item.Flags & FixedCountArrayFlag) != 0)
        {
            countValue = item.Count.ToString(CultureInfo.InvariantCulture);

            return true;
        }

        if ((item.InType & WevtTypeNames.ArrayFlag) != 0)
        {
            countValue = (item.Count == 0 ? 1 : item.Count).ToString(CultureInfo.InvariantCulture);

            return true;
        }

        return true;
    }

    /// <summary>
    ///     Resolves the optional <c>length</c> attribute. The pinned form is a field-name reference: when flags@0 carries
    ///     the reference bit (0x4), length@14 is the 0-based index of the field whose value supplies this field's length, and
    ///     the manifest emits <c>length="&lt;that field's name&gt;"</c>. Returns <see langword="false" /> (fail closed) for a
    ///     fixed numeric length (0x2) or an out-of-range / unnamed reference; returns <see langword="true" /> with a null
    ///     <paramref name="lengthName" /> when the field carries no length attribute.
    /// </summary>
    private static bool TryResolveLengthReference(
        IReadOnlyList<WevtTemplateItem> items,
        WevtTemplateItem item,
        out string? lengthName)
    {
        lengthName = null;

        if ((item.Flags & LengthFieldReferenceFlag) != 0)
        {
            if (item.LengthRefIndex >= items.Count) { return false; }

            string referencedName = items[item.LengthRefIndex].Name;

            if (referencedName.Length == 0) { return false; }

            lengthName = referencedName;

            return true;
        }

        // A fixed numeric length (length@14 is a byte count, not a field reference) is outside the pinned field-name
        // form, so the template fails closed instead of emitting a guessed numeric length.
        return (item.Flags & FixedLengthFlag) == 0;
    }
}
