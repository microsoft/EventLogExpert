// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace EventLogExpert.Localization.Plural;

public sealed class IcuMessageFormatter
{
    private const int MaxCacheSize = 2048;

    private static readonly ConcurrentDictionary<string, ParsedMessage> s_cache = new(StringComparer.Ordinal);

    public string Format(
        string pattern,
        IReadOnlyDictionary<string, object?> args,
        CultureInfo numberCulture,
        CultureInfo pluralCulture)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(numberCulture);
        ArgumentNullException.ThrowIfNull(pluralCulture);

        ParsedMessage parsedMessage = GetParsedMessage(pattern);

        return parsedMessage.Format(args, numberCulture, pluralCulture);
    }

    internal static bool TryParse(string pattern, out ParsedMessage? parsedMessage, out IcuMessageException? exception)
    {
        try
        {
            parsedMessage = Parser.Parse(pattern);
            exception = null;

            return true;
        }
        catch (IcuMessageException parseException)
        {
            parsedMessage = null;
            exception = parseException;

            return false;
        }
    }

    private static object? GetArgument(IReadOnlyDictionary<string, object?> args, string name) =>
        !args.TryGetValue(name, out object? value) ?
            throw new IcuMessageException($"Required argument {name} was not supplied.") :
            value;

    private static ParsedMessage GetParsedMessage(string pattern)
    {
        if (s_cache.TryGetValue(pattern, out ParsedMessage? cached))
        {
            return cached;
        }

        ParsedMessage parsed = Parser.Parse(pattern);

        // Bound the cache: patterns come from a finite resource set, so a cap that is never reached in practice
        // still protects the public API from unbounded growth on caller-supplied dynamic patterns.
        if (s_cache.Count < MaxCacheSize)
        {
            s_cache.TryAdd(pattern, parsed);
        }

        return parsed;
    }

    internal abstract class MessagePart
    {
        internal abstract void AppendTo(
            StringBuilder builder,
            IReadOnlyDictionary<string, object?> args,
            CultureInfo numberCulture,
            CultureInfo pluralCulture);
    }

    internal sealed class ParsedMessage
    {
        private readonly MessagePart[] _parts;

        internal ParsedMessage(MessagePart[] parts) => _parts = parts;

        internal void AppendTo(
            StringBuilder builder,
            IReadOnlyDictionary<string, object?> args,
            CultureInfo numberCulture,
            CultureInfo pluralCulture)
        {
            foreach (MessagePart part in _parts)
            {
                part.AppendTo(builder, args, numberCulture, pluralCulture);
            }
        }

        internal string Format(
            IReadOnlyDictionary<string, object?> args,
            CultureInfo numberCulture,
            CultureInfo pluralCulture)
        {
            StringBuilder builder = new();
            AppendTo(builder, args, numberCulture, pluralCulture);

            return builder.ToString();
        }
    }

    private sealed class ArgumentPart : MessagePart
    {
        private readonly string _format;
        private readonly string _name;

        internal ArgumentPart(string name, string format)
        {
            _format = format;
            _name = name;
        }

        internal override void AppendTo(
            StringBuilder builder,
            IReadOnlyDictionary<string, object?> args,
            CultureInfo numberCulture,
            CultureInfo pluralCulture)
        {
            object? value = GetArgument(args, _name);

            if (value is null)
            {
                return;
            }

            try
            {
                if (_format.Length == 0)
                {
                    builder.Append(value is IFormattable formattable ? formattable.ToString(null, numberCulture) :
                        value.ToString());

                    return;
                }

                if (value is not IFormattable formattedValue)
                {
                    throw new FormatException($"Argument {_name} does not support .NET format strings.");
                }

                builder.Append(formattedValue.ToString(_format, numberCulture));
            }
            catch (Exception exception) when (exception is FormatException
                or InvalidCastException
                or ArgumentException
                or OverflowException)
            {
                throw new IcuMessageException($"Failed to format argument {_name}.", exception);
            }
        }
    }

    private sealed class Parser
    {
        private readonly string _pattern;

        private int _position;

        private Parser(string pattern) => _pattern = pattern;

        private char Current => _pattern[_position];

        private bool IsAtEnd => _position >= _pattern.Length;

        internal static ParsedMessage Parse(string pattern)
        {
            Parser parser = new(pattern);
            ParsedMessage message = parser.ParseMessage(isBranch: false);

            return !parser.IsAtEnd ? throw parser.Error("Unexpected trailing input.") : message;
        }

        private static void FlushText(List<MessagePart> parts, StringBuilder textBuilder)
        {
            if (textBuilder.Length == 0)
            {
                return;
            }

            parts.Add(new TextPart(textBuilder.ToString()));
            textBuilder.Clear();
        }

        private static string GetCategoryName(PluralCategory category) =>
            category switch
            {
                PluralCategory.Zero => "zero",
                PluralCategory.One => "one",
                PluralCategory.Two => "two",
                PluralCategory.Few => "few",
                PluralCategory.Many => "many",
                PluralCategory.Other => "other",
                _ => throw new InvalidOperationException($"Unknown plural category {category}."),
            };

        private IcuMessageException Error(string message) =>
            new($"{message} Position {_position.ToString(CultureInfo.InvariantCulture)}.");

        private void Expect(char expected)
        {
            if (!TryConsume(expected))
            {
                throw Error($"Expected '{expected}'.");
            }
        }

        private MessagePart ParseArgument()
        {
            Expect('{');
            SkipWhiteSpace();
            string name = ParseName();
            SkipWhiteSpace();

            if (TryConsume('}'))
            {
                return new ArgumentPart(name, string.Empty);
            }

            if (TryConsume(':'))
            {
                string format = ParseNetFormat();

                return new ArgumentPart(name, format);
            }

            if (TryConsume(','))
            {
                SkipWhiteSpace();
                string argumentKind = ParseIdentifier();

                if (!string.Equals(argumentKind, "plural", StringComparison.Ordinal))
                {
                    throw Error($"Unsupported ICU argument kind {argumentKind}.");
                }

                SkipWhiteSpace();
                Expect(',');

                return ParsePlural(name);
            }

            throw Error("Expected argument terminator, format, or plural block.");
        }

        private string ParseIdentifier()
        {
            if (IsAtEnd || !(char.IsAsciiLetter(Current) || Current == '_'))
            {
                throw Error("Expected identifier.");
            }

            int start = _position;
            _position++;

            while (!IsAtEnd && (char.IsAsciiLetterOrDigit(Current) || Current == '_'))
            {
                _position++;
            }

            return _pattern[start.._position];
        }

        private ParsedMessage ParseMessage(bool isBranch)
        {
            List<MessagePart> parts = [];
            StringBuilder textBuilder = new();

            while (!IsAtEnd)
            {
                if (isBranch && Current == '}')
                {
                    FlushText(parts, textBuilder);
                    _position++;

                    return new ParsedMessage([.. parts]);
                }

                if (Current == '{')
                {
                    if (!isBranch && Peek('{'))
                    {
                        textBuilder.Append('{');
                        _position += 2;

                        continue;
                    }

                    FlushText(parts, textBuilder);
                    parts.Add(ParseArgument());
                    continue;
                }

                if (Current == '}')
                {
                    if (!isBranch && Peek('}'))
                    {
                        textBuilder.Append('}');
                        _position += 2;

                        continue;
                    }

                    throw Error("Unmatched closing brace.");
                }

                if (isBranch && Current == '#')
                {
                    throw Error("Bare # is not supported in plural branches.");
                }

                textBuilder.Append(Current);
                _position++;
            }

            if (isBranch)
            {
                throw Error("Unclosed plural branch.");
            }

            FlushText(parts, textBuilder);

            return new ParsedMessage([.. parts]);
        }

        private string ParseName()
        {
            if (IsAtEnd)
            {
                throw Error("Expected argument name.");
            }

            if (!char.IsDigit(Current))
            {
                return ParseIdentifier();
            }

            int start = _position;

            while (!IsAtEnd && char.IsDigit(Current))
            {
                _position++;
            }

            return _pattern[start.._position];
        }

        private string ParseNetFormat()
        {
            int start = _position;

            while (!IsAtEnd && Current != '}')
            {
                if (Current == '{')
                {
                    throw Error("Braces are not allowed in .NET format strings.");
                }

                _position++;
            }

            if (IsAtEnd)
            {
                throw Error("Unclosed .NET format string.");
            }

            string format = _pattern[start.._position];
            Expect('}');

            return format;
        }

        private PluralPart ParsePlural(string name)
        {
            Dictionary<long, ParsedMessage> exactBranches = [];
            Dictionary<PluralCategory, ParsedMessage> categoryBranches = [];
            ParsedMessage? otherBranch = null;
            bool hasAnySelector = false;

            while (true)
            {
                SkipWhiteSpace();

                if (TryConsume('}'))
                {
                    if (!hasAnySelector)
                    {
                        throw Error("Plural block must contain at least one selector.");
                    }

                    if (otherBranch is null)
                    {
                        throw Error("Plural block must contain an other selector.");
                    }

                    return new PluralPart(name, exactBranches, categoryBranches, otherBranch);
                }

                hasAnySelector = true;
                PluralSelector selector = ParseSelector();
                SkipWhiteSpace();
                Expect('{');
                ParsedMessage branch = ParseMessage(isBranch: true);

                if (selector.ExactValue.HasValue)
                {
                    if (!exactBranches.TryAdd(selector.ExactValue.Value, branch))
                    {
                        throw Error(
                            $"Duplicate plural selector ={selector.ExactValue.Value.ToString(CultureInfo.InvariantCulture)}.");
                    }
                }
                else
                {
                    if (!categoryBranches.TryAdd(selector.Category, branch))
                    {
                        throw Error($"Duplicate plural selector {GetCategoryName(selector.Category)}.");
                    }

                    if (selector.Category == PluralCategory.Other)
                    {
                        otherBranch = branch;
                    }
                }
            }
        }

        private PluralSelector ParseSelector()
        {
            if (TryConsume('='))
            {
                int start = _position;
                _ = TryConsume('-');

                if (IsAtEnd || !char.IsDigit(Current))
                {
                    throw Error("Exact plural selector must be numeric.");
                }

                while (!IsAtEnd && char.IsDigit(Current))
                {
                    _position++;
                }

                string selectorValue = _pattern[start.._position];

                try
                {
                    return PluralSelector.ForExact(long.Parse(selectorValue,
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture));
                }
                catch (Exception exception) when (exception is FormatException or OverflowException)
                {
                    throw new IcuMessageException($"Exact plural selector {selectorValue} is not numeric.", exception);
                }
            }

            string selector = ParseIdentifier();

            return selector switch
            {
                "zero" => PluralSelector.ForCategory(PluralCategory.Zero),
                "one" => PluralSelector.ForCategory(PluralCategory.One),
                "two" => PluralSelector.ForCategory(PluralCategory.Two),
                "few" => PluralSelector.ForCategory(PluralCategory.Few),
                "many" => PluralSelector.ForCategory(PluralCategory.Many),
                "other" => PluralSelector.ForCategory(PluralCategory.Other),
                _ => throw Error($"Unknown plural selector {selector}."),
            };
        }

        private bool Peek(char expected) => _position + 1 < _pattern.Length && _pattern[_position + 1] == expected;

        private void SkipWhiteSpace()
        {
            while (!IsAtEnd && char.IsWhiteSpace(Current))
            {
                _position++;
            }
        }

        private bool TryConsume(char expected)
        {
            if (IsAtEnd || Current != expected)
            {
                return false;
            }

            _position++;

            return true;
        }
    }

    private sealed class PluralPart : MessagePart
    {
        private readonly Dictionary<PluralCategory, ParsedMessage> _categoryBranches;
        private readonly Dictionary<long, ParsedMessage> _exactBranches;
        private readonly string _name;
        private readonly ParsedMessage _otherBranch;

        internal PluralPart(
            string name,
            Dictionary<long, ParsedMessage> exactBranches,
            Dictionary<PluralCategory, ParsedMessage> categoryBranches,
            ParsedMessage otherBranch)
        {
            _categoryBranches = categoryBranches;
            _exactBranches = exactBranches;
            _name = name;
            _otherBranch = otherBranch;
        }

        internal override void AppendTo(
            StringBuilder builder,
            IReadOnlyDictionary<string, object?> args,
            CultureInfo numberCulture,
            CultureInfo pluralCulture)
        {
            object? value = GetArgument(args, _name);

            if (value is null)
            {
                throw new IcuMessageException($"Plural selector argument {_name} is null.");
            }

            decimal selectorValue = GetSelectorValue(value, _name);

            if (decimal.Truncate(selectorValue) == selectorValue &&
                selectorValue >= long.MinValue &&
                selectorValue <= long.MaxValue &&
                _exactBranches.TryGetValue((long)selectorValue, out ParsedMessage? exactBranch))
            {
                exactBranch.AppendTo(builder, args, numberCulture, pluralCulture);
                return;
            }

            PluralOperands operands = GetSelectorOperands(value, selectorValue);
            PluralCategory category = CldrPluralRules.Select(pluralCulture, operands);

            ParsedMessage branch = _categoryBranches.GetValueOrDefault(category, _otherBranch);

            branch.AppendTo(builder, args, numberCulture, pluralCulture);
        }

        private static PluralOperands GetSelectorOperands(object value, decimal selectorValue) =>
            value switch
            {
                byte byteValue => PluralOperands.FromLong(byteValue),
                sbyte signedByteValue => PluralOperands.FromLong(signedByteValue),
                short shortValue => PluralOperands.FromLong(shortValue),
                ushort unsignedShortValue => PluralOperands.FromLong(unsignedShortValue),
                int integerValue => PluralOperands.FromLong(integerValue),
                uint unsignedIntegerValue => PluralOperands.FromLong(unsignedIntegerValue),
                long longValue => PluralOperands.FromLong(longValue),
                _ => PluralOperands.FromDecimal(selectorValue),
            };

        private static decimal GetSelectorValue(object value, string name)
        {
            try
            {
                return value switch
                {
                    decimal decimalValue => decimalValue,
                    byte byteValue => byteValue,
                    sbyte signedByteValue => signedByteValue,
                    short shortValue => shortValue,
                    ushort unsignedShortValue => unsignedShortValue,
                    int integerValue => integerValue,
                    uint unsignedIntegerValue => unsignedIntegerValue,
                    long longValue => longValue,
                    ulong unsignedLongValue => unsignedLongValue,
                    float floatValue => (decimal)floatValue,
                    double doubleValue => (decimal)doubleValue,
                    _ => throw new IcuMessageException(
                        $"Plural selector argument {name} is not a supported numeric type ({value.GetType().Name})."),
                };
            }
            catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
            {
                throw new IcuMessageException($"Plural selector argument {name} is not numeric.", exception);
            }
        }
    }

    private sealed class TextPart : MessagePart
    {
        private readonly string _text;

        internal TextPart(string text) => _text = text;

        internal override void AppendTo(
            StringBuilder builder,
            IReadOnlyDictionary<string, object?> args,
            CultureInfo numberCulture,
            CultureInfo pluralCulture) =>
            builder.Append(_text);
    }

    private readonly record struct PluralSelector(long? ExactValue, PluralCategory Category)
    {
        internal static PluralSelector ForExact(long exactValue) => new(exactValue, PluralCategory.Other);

        internal static PluralSelector ForCategory(PluralCategory category) => new(null, category);
    }
}
