// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Buffers;
using System.Globalization;
using System.Text;

namespace EventLogExpert.Eventing.Structured;

internal enum XmlSpanNode : byte
{
    Element,
    EndElement
}

/// <summary>
///     Forward-only, DOM-free scanner over rendered event XML. Elements are read by local name (any namespace prefix
///     is dropped), and attribute names, attribute values, and element text are exposed as spans into the source so a
///     caller extracts only the values it wants, allocating a string just for those. Handles the shapes wevtapi emits: the
///     XML declaration, comments, CDATA, single- or double-quoted attributes, and self-closing elements.
/// </summary>
internal ref struct XmlSpanScanner(ReadOnlySpan<char> xml)
{
    private static readonly SearchValues<char> s_whitespace = SearchValues.Create(" \t\r\n");

    private readonly ReadOnlySpan<char> _xml = xml;
    private int _position;
    private ReadOnlySpan<char> _attributes;
    private int _contentStart = -1;

    public XmlSpanNode NodeType { get; private set; }

    public ReadOnlySpan<char> LocalName { get; private set; }

    public bool IsEmptyElement { get; private set; }

    public bool Read()
    {
        IsEmptyElement = false;
        _attributes = default;
        _contentStart = -1;

        while (_position < _xml.Length)
        {
            int relativeStart = _xml[_position..].IndexOf('<');

            if (relativeStart < 0) { break; }

            _position += relativeStart;
            ReadOnlySpan<char> rest = _xml[_position..];

            if (rest.StartsWith("<?")) { Skip("?>"); continue; }

            if (rest.StartsWith("<!--")) { Skip("-->"); continue; }

            if (rest.StartsWith("<![CDATA[")) { Skip("]]>"); continue; }

            if (rest.StartsWith("<!")) { SkipTag(); continue; }

            int closeRelative = IndexOfTagEnd(rest);

            if (closeRelative < 0) { break; }

            int closeAbsolute = _position + closeRelative;

            if (rest[1] == '/')
            {
                LocalName = LocalNameOf(_xml[(_position + 2)..closeAbsolute].Trim());
                _position = closeAbsolute + 1;
                NodeType = XmlSpanNode.EndElement;

                return true;
            }

            ReadOnlySpan<char> tag = _xml[(_position + 1)..closeAbsolute];

            if (tag.EndsWith("/"))
            {
                tag = tag[..^1];
                IsEmptyElement = true;
            }

            int nameEnd = tag.IndexOfAny(s_whitespace);

            LocalName = LocalNameOf(nameEnd < 0 ? tag : tag[..nameEnd]);
            _attributes = nameEnd < 0 ? default : tag[nameEnd..];
            _position = closeAbsolute + 1;
            _contentStart = _position;
            NodeType = XmlSpanNode.Element;

            return true;
        }

        _position = _xml.Length;

        return false;
    }

    public XmlAttributeLister Attributes => new(_attributes);

    /// <summary>
    ///     Text content of the element just read: entity-encoded text up to the next tag, or, when
    ///     <paramref name="isCData" /> is set, the literal contents of a leading CDATA section.
    /// </summary>
    public readonly ReadOnlySpan<char> RawText(out bool isCData)
    {
        isCData = false;

        if (_contentStart < 0) { return default; }

        ReadOnlySpan<char> rest = _xml[_contentStart..];
        int relativeEnd = rest.IndexOf('<');

        if (relativeEnd < 0 || !rest[relativeEnd..].StartsWith("<![CDATA["))
        {
            return relativeEnd < 0 ? rest : rest[..relativeEnd];
        }

        isCData = true;

        ReadOnlySpan<char> cdataRest = rest[(relativeEnd + 9)..];
        int cdataEnd = cdataRest.IndexOf("]]>");

        return cdataEnd < 0 ? cdataRest : cdataRest[..cdataEnd];

    }

    /// <summary>Materializes a value span, decoding XML entities only when the span actually contains one.</summary>
    public static string DecodeToString(ReadOnlySpan<char> value)
    {
        if (value.IndexOf('&') < 0) { return value.ToString(); }

        var builder = new StringBuilder(value.Length);

        for (int index = 0; index < value.Length;)
        {
            char current = value[index];

            if (current != '&') { builder.Append(current); index++; continue; }

            int semicolon = value[index..].IndexOf(';');

            if (semicolon < 0) { builder.Append(current); index++; continue; }

            ReadOnlySpan<char> entity = value.Slice(index + 1, semicolon - 1);

            if (TryDecodeEntity(entity, out char decoded))
            {
                builder.Append(decoded);
            }
            else
            {
                builder.Append(value.Slice(index, semicolon + 1));
            }

            index += semicolon + 1;
        }

        return builder.ToString();
    }

    private static bool TryDecodeEntity(ReadOnlySpan<char> entity, out char decoded)
    {
        switch (entity)
        {
            case "amp": decoded = '&'; return true;
            case "lt": decoded = '<'; return true;
            case "gt": decoded = '>'; return true;
            case "quot": decoded = '"'; return true;
            case "apos": decoded = '\''; return true;
        }

        if (entity.Length >= 2 && entity[0] == '#')
        {
            bool hex = entity[1] is 'x' or 'X';
            ReadOnlySpan<char> digits = hex ? entity[2..] : entity[1..];

            if (int.TryParse(digits,
                    hex ? NumberStyles.HexNumber : NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int code) &&
                code is > 0 and <= char.MaxValue)
            {
                decoded = (char)code;

                return true;
            }
        }

        decoded = '\0';

        return false;
    }

    private static ReadOnlySpan<char> LocalNameOf(ReadOnlySpan<char> name)
    {
        int colon = name.LastIndexOf(':');

        return colon < 0 ? name : name[(colon + 1)..];
    }

    // Finds the '>' that closes a tag, ignoring any '>' inside a single- or double-quoted attribute value.
    private static int IndexOfTagEnd(ReadOnlySpan<char> tag)
    {
        char quote = '\0';

        for (int index = 1; index < tag.Length; index++)
        {
            char character = tag[index];

            if (quote != '\0')
            {
                if (character == quote) { quote = '\0'; }
            }
            else if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (character == '>')
            {
                return index;
            }
        }

        return -1;
    }

    private void Skip(ReadOnlySpan<char> terminator)
    {
        int relativeEnd = _xml[_position..].IndexOf(terminator);

        _position = relativeEnd < 0 ? _xml.Length : _position + relativeEnd + terminator.Length;
    }

    private void SkipTag()
    {
        int relativeEnd = _xml[_position..].IndexOf('>');

        _position = relativeEnd < 0 ? _xml.Length : _position + relativeEnd + 1;
    }
}

/// <summary>Forward-only enumerator over an element's attributes, skipping xmlns declarations, without allocating.</summary>
internal ref struct XmlAttributeLister(ReadOnlySpan<char> attributes)
{
    private static readonly SearchValues<char> s_whitespace = SearchValues.Create(" \t\r\n");

    private ReadOnlySpan<char> _remaining = attributes;

    public ReadOnlySpan<char> LocalName { get; private set; }

    public ReadOnlySpan<char> RawValue { get; private set; }

    public bool MoveNext()
    {
        while (true)
        {
            int nameStart = IndexOfNonWhitespace(_remaining);

            if (nameStart < 0) { return false; }

            ReadOnlySpan<char> fromName = _remaining[nameStart..];
            int equals = fromName.IndexOf('=');

            if (equals < 0) { return false; }

            ReadOnlySpan<char> name = fromName[..equals].TrimEnd();
            ReadOnlySpan<char> afterEquals = fromName[(equals + 1)..];
            int quoteStart = IndexOfNonWhitespace(afterEquals);

            if (quoteStart < 0) { return false; }

            char quote = afterEquals[quoteStart];

            if (quote is not ('\'' or '"')) { return false; }

            ReadOnlySpan<char> fromQuote = afterEquals[(quoteStart + 1)..];
            int valueEnd = fromQuote.IndexOf(quote);

            if (valueEnd < 0) { return false; }

            _remaining = fromQuote[(valueEnd + 1)..];

            if (name.StartsWith("xmlns") && (name.Length == 5 || name[5] == ':')) { continue; }

            LocalName = LocalNameOf(name);
            RawValue = fromQuote[..valueEnd];

            return true;
        }
    }

    private static ReadOnlySpan<char> LocalNameOf(ReadOnlySpan<char> name)
    {
        int colon = name.LastIndexOf(':');

        return colon < 0 ? name : name[(colon + 1)..];
    }

    private static int IndexOfNonWhitespace(ReadOnlySpan<char> span)
    {
        for (int index = 0; index < span.Length; index++)
        {
            if (!s_whitespace.Contains(span[index])) { return index; }
        }

        return -1;
    }
}
