// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Provider.Resolution;

// Non-canonical data nodes fail closed to raw spans; count and struct are render-dead.
public ref struct TemplateFieldReader(ReadOnlySpan<char> template)
{
    private const string DataTag = "<data";

    private ReadOnlySpan<char> _remaining = template;

    public TemplateField Current { get; private set; }

    public readonly TemplateFieldReader GetEnumerator() => this;

    public bool MoveNext()
    {
        ReadOnlySpan<char> span = _remaining;
        int searchStart = 0;

        while (searchStart < span.Length)
        {
            int relative = span[searchStart..].IndexOf(DataTag, StringComparison.OrdinalIgnoreCase);

            if (relative == -1) { break; }

            int dataIndex = searchStart + relative;
            int afterTag = dataIndex + DataTag.Length;

            // Reject "<dataSource" and similar; the tag must be delimited.
            if (afterTag < span.Length)
            {
                char next = span[afterTag];

                if (next is not (' ' or '\t' or '\r' or '\n' or '/' or '>'))
                {
                    searchStart = afterTag;

                    continue;
                }
            }

            ReadOnlySpan<char> fromData = span[dataIndex..];
            int closeIndex = FindElementEnd(fromData);

            if (closeIndex == -1)
            {
                // Unterminated elements fail closed to raw spans rather than dropping content.
                Current = TemplateField.RawElement(fromData);
                _remaining = default;

                return true;
            }

            int elementEnd = closeIndex > 0 && fromData[closeIndex - 1] == '/' ? closeIndex - 1 : closeIndex;
            Current = ParseElement(fromData[..elementEnd]);
            _remaining = span[(dataIndex + closeIndex + 1)..];

            return true;
        }

        _remaining = default;

        return false;
    }

    private static int FindElementEnd(ReadOnlySpan<char> fromData)
    {
        char openQuote = '\0';

        for (int i = DataTag.Length; i < fromData.Length; i++)
        {
            char c = fromData[i];

            if (openQuote != '\0')
            {
                if (c == openQuote) { openQuote = '\0'; }
            }
            else if (c is '"' or '\'')
            {
                openQuote = c;
            }
            else if (c == '>')
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsSignatureAttribute(ReadOnlySpan<char> attributeName) =>
        attributeName.Equals("name", StringComparison.OrdinalIgnoreCase) ||
        attributeName.Equals("inType", StringComparison.OrdinalIgnoreCase) ||
        attributeName.Equals("outType", StringComparison.OrdinalIgnoreCase) ||
        attributeName.Equals("length", StringComparison.OrdinalIgnoreCase) ||
        attributeName.Equals("map", StringComparison.OrdinalIgnoreCase);

    private static TemplateField ParseElement(ReadOnlySpan<char> element)
    {
        ReadOnlySpan<char> name = default;
        ReadOnlySpan<char> inType = default;
        ReadOnlySpan<char> outType = default;
        ReadOnlySpan<char> length = default;
        ReadOnlySpan<char> map = default;

        int pos = DataTag.Length;

        while (pos < element.Length)
        {
            if (element[pos] is ' ' or '\t' or '\r' or '\n' or '/')
            {
                pos++;

                continue;
            }

            int nameStart = pos;

            while (pos < element.Length && element[pos] is not ('=' or ' ' or '\t' or '\r' or '\n' or '/')) { pos++; }

            ReadOnlySpan<char> attributeName = element[nameStart..pos];
            bool signature = IsSignatureAttribute(attributeName);

            while (pos < element.Length && element[pos] is ' ' or '\t' or '\r' or '\n') { pos++; }

            if (pos >= element.Length || element[pos] != '=')
            {
                // Signature attributes without values fail closed to preserve distinct raw elements.
                if (signature) { return TemplateField.RawElement(element); }

                continue;
            }

            pos++;

            while (pos < element.Length && element[pos] is ' ' or '\t' or '\r' or '\n') { pos++; }

            if (pos >= element.Length || element[pos] != '"')
            {
                // Signature attributes require double-quoted values; otherwise fail closed.
                if (signature) { return TemplateField.RawElement(element); }

                pos = SkipValue(element, pos);

                continue;
            }

            pos++;
            int valueStart = pos;

            while (pos < element.Length && element[pos] != '"') { pos++; }

            ReadOnlySpan<char> value = element[valueStart..pos];

            if (pos < element.Length) { pos++; }

            if (!signature) { continue; }

            if (attributeName.Equals("name", StringComparison.OrdinalIgnoreCase)) { name = value; }
            else if (attributeName.Equals("inType", StringComparison.OrdinalIgnoreCase)) { inType = value; }
            else if (attributeName.Equals("outType", StringComparison.OrdinalIgnoreCase)) { outType = value; }
            else if (attributeName.Equals("length", StringComparison.OrdinalIgnoreCase)) { length = value; }
            else if (attributeName.Equals("map", StringComparison.OrdinalIgnoreCase)) { map = value; }
        }

        // All-empty signatures fail closed so distinct raw elements do not collapse together.
        if (name.IsEmpty && inType.IsEmpty && outType.IsEmpty && length.IsEmpty && map.IsEmpty)
        {
            return TemplateField.RawElement(element);
        }

        return TemplateField.Parsed(name, inType, outType, length, map);
    }

    private static int SkipValue(ReadOnlySpan<char> element, int pos)
    {
        if (pos >= element.Length) { return pos; }

        char quote = element[pos];

        if (quote is '"' or '\'')
        {
            pos++;

            while (pos < element.Length && element[pos] != quote) { pos++; }

            return pos < element.Length ? pos + 1 : pos;
        }

        while (pos < element.Length && element[pos] is not (' ' or '\t' or '\r' or '\n')) { pos++; }

        return pos;
    }
}
