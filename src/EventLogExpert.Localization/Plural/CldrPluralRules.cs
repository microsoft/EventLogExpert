// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace EventLogExpert.Localization.Plural;

public static class CldrPluralRules
{
    internal const string ResourceNameSuffix = ".Plural.cldr-plurals-cardinal.json";

    private static readonly Lazy<IReadOnlyDictionary<string, LocaleRules>> s_localeRules =
        new(LoadRules, LazyThreadSafetyMode.ExecutionAndPublication);

    public static PluralCategory Select(CultureInfo culture, PluralOperands operands)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return ResolveRules(culture).Select(operands);
    }

    public static PluralCategory Select(CultureInfo culture, long value) =>
        Select(culture, PluralOperands.FromLong(value));

    public static PluralCategory Select(CultureInfo culture, decimal value) =>
        Select(culture, PluralOperands.FromDecimal(value));

    internal static string GetEmbeddedResourceName()
    {
        Assembly assembly = typeof(CldrPluralRules).Assembly;
        string[] resourceNames = assembly.GetManifestResourceNames();

        return resourceNames.Single(name => name.EndsWith(ResourceNameSuffix, StringComparison.Ordinal));
    }

    internal static PluralCategory Select(string localeName, PluralOperands operands)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localeName);

        return ResolveRules(localeName).Select(operands);
    }

    private static IReadOnlyDictionary<string, LocaleRules> LoadRules()
    {
        Assembly assembly = typeof(CldrPluralRules).Assembly;
        string resourceName = GetEmbeddedResourceName();

        using Stream stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException($"Embedded resource {resourceName} was not found.");

        using JsonDocument document = JsonDocument.Parse(stream);

        JsonElement pluralRulesElement = document.RootElement
            .GetProperty("supplemental")
            .GetProperty("plurals-type-cardinal");

        Dictionary<string, LocaleRules> localeRules = new(StringComparer.Ordinal);

        foreach (JsonProperty localeProperty in pluralRulesElement.EnumerateObject())
        {
            Dictionary<PluralCategory, RuleExpression> categoryRules = new();

            foreach (JsonProperty ruleProperty in localeProperty.Value.EnumerateObject())
            {
                const string prefix = "pluralRule-count-";

                if (!ruleProperty.Name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string categoryName = ruleProperty.Name[prefix.Length..];

                if (!TryParseCategory(categoryName, out PluralCategory category) || category == PluralCategory.Other)
                {
                    continue;
                }

                string ruleText = RemoveSamples(ruleProperty.Value.GetString() ?? string.Empty);

                if (ruleText.Length > 0)
                {
                    categoryRules.Add(category, RuleParser.Parse(ruleText));
                }
            }

            localeRules.Add(localeProperty.Name, new LocaleRules(categoryRules));
        }

        return localeRules;
    }

    private static string RemoveSamples(string ruleText)
    {
        int sampleIndex = ruleText.IndexOf('@', StringComparison.Ordinal);

        return (sampleIndex >= 0 ? ruleText[..sampleIndex] : ruleText).Trim();
    }

    private static LocaleRules ResolveRules(CultureInfo culture)
    {
        IReadOnlyDictionary<string, LocaleRules> rules = s_localeRules.Value;

        for (CultureInfo currentCulture = culture;; currentCulture = currentCulture.Parent)
        {
            if (!string.IsNullOrEmpty(currentCulture.Name) &&
                rules.TryGetValue(currentCulture.Name, out LocaleRules? localeRules))
            {
                return localeRules;
            }

            if (currentCulture.Equals(CultureInfo.InvariantCulture))
            {
                return LocaleRules.OtherOnly;
            }
        }
    }

    private static LocaleRules ResolveRules(string localeName)
    {
        IReadOnlyDictionary<string, LocaleRules> rules = s_localeRules.Value;

        for (string currentLocaleName = localeName; currentLocaleName.Length > 0;)
        {
            if (rules.TryGetValue(currentLocaleName, out LocaleRules? localeRules))
            {
                return localeRules;
            }

            int separatorIndex = currentLocaleName.LastIndexOf('-');
            currentLocaleName = separatorIndex < 0 ? string.Empty : currentLocaleName[..separatorIndex];
        }

        return LocaleRules.OtherOnly;
    }

    private static bool SetCategory(PluralCategory value, out PluralCategory category, bool result = true)
    {
        category = value;

        return result;
    }

    private static bool TryParseCategory(string value, out PluralCategory category) =>
        value switch
        {
            "zero" => SetCategory(PluralCategory.Zero, out category),
            "one" => SetCategory(PluralCategory.One, out category),
            "two" => SetCategory(PluralCategory.Two, out category),
            "few" => SetCategory(PluralCategory.Few, out category),
            "many" => SetCategory(PluralCategory.Many, out category),
            "other" => SetCategory(PluralCategory.Other, out category),
            _ => SetCategory(default, out category, false),
        };

    private sealed class LocaleRules
    {
        internal static readonly LocaleRules OtherOnly = new(new Dictionary<PluralCategory, RuleExpression>());

        private static readonly PluralCategory[] s_evaluationOrder =
        [
            PluralCategory.Zero,
            PluralCategory.One,
            PluralCategory.Two,
            PluralCategory.Few,
            PluralCategory.Many,
        ];

        private readonly IReadOnlyDictionary<PluralCategory, RuleExpression> _rules;

        internal LocaleRules(IReadOnlyDictionary<PluralCategory, RuleExpression> rules) => _rules = rules;

        internal PluralCategory Select(PluralOperands operands)
        {
            if (operands.C != 0 || operands.E != 0)
            {
                foreach (PluralCategory category in s_evaluationOrder)
                {
                    if (_rules.TryGetValue(category, out RuleExpression? compactRule) &&
                        compactRule.HasCompactOperand &&
                        compactRule.Evaluate(operands))
                    {
                        return category;
                    }
                }

                return PluralCategory.Other;
            }

            foreach (PluralCategory category in s_evaluationOrder)
            {
                if (_rules.TryGetValue(category, out RuleExpression? rule) && rule.Evaluate(operands))
                {
                    return category;
                }
            }

            return PluralCategory.Other;
        }
    }

    private sealed class RuleExpression
    {
        private readonly AndCondition[][] _orConditions;

        internal RuleExpression(AndCondition[][] orConditions) => _orConditions = orConditions;

        internal bool HasCompactOperand =>
            _orConditions.Any(andConditions => andConditions.Any(condition => condition.UsesCompactOperand));

        internal bool Evaluate(PluralOperands operands)
        {
            foreach (AndCondition[] andConditions in _orConditions)
            {
                bool allConditionsMatch = true;

                foreach (AndCondition condition in andConditions)
                {
                    if (!condition.Evaluate(operands))
                    {
                        allConditionsMatch = false;

                        break;
                    }
                }

                if (allConditionsMatch)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private sealed class RuleParser
    {
        private readonly string[] _tokens;
        private int _position;

        private RuleParser(string ruleText) => _tokens = [.. Tokenize(ruleText)];

        internal static RuleExpression Parse(string ruleText)
        {
            RuleParser parser = new(ruleText);
            RuleExpression expression = parser.ParseRule();
            parser.ExpectEnd();

            return expression;
        }

        private static decimal ParseDecimal(string value) =>
            decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);

        private static IEnumerable<string> Tokenize(string ruleText)
        {
            for (int index = 0; index < ruleText.Length;)
            {
                char current = ruleText[index];

                if (char.IsWhiteSpace(current))
                {
                    index++;

                    continue;
                }

                if (current == '!' && index + 1 < ruleText.Length && ruleText[index + 1] == '=')
                {
                    yield return "!=";

                    index += 2;
                    
                    continue;
                }

                if (current == '.' && index + 1 < ruleText.Length && ruleText[index + 1] == '.')
                {
                    yield return "..";

                    index += 2;
                    
                    continue;
                }

                if (current is '=' or '%' or ',')
                {
                    yield return current.ToString();

                    index++;
                    
                    continue;
                }

                int start = index;

                while (index < ruleText.Length &&
                    !char.IsWhiteSpace(ruleText[index]) &&
                    ruleText[index] is not '=' and not '%' and not ',' and not '!')
                {
                    if (ruleText[index] == '.' && index + 1 < ruleText.Length && ruleText[index + 1] == '.')
                    {
                        break;
                    }

                    index++;
                }

                if (index == start)
                {
                    throw new InvalidOperationException($"Unexpected character '{current}' in CLDR plural rule.");
                }

                yield return ruleText[start..index];
            }
        }

        private string Consume() =>
            _position >= _tokens.Length ?
                throw new InvalidOperationException("Unexpected end of CLDR plural rule.") :
                _tokens[_position++];

        private void ExpectEnd()
        {
            if (_position != _tokens.Length)
            {
                throw new InvalidOperationException($"Unexpected CLDR token {_tokens[_position]}.");
            }
        }

        private AndCondition ParseCondition()
        {
            string operandToken = Consume();

            if (operandToken.Length != 1 || "nivwftce".IndexOf(operandToken[0], StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException($"Invalid CLDR operand token {operandToken}.");
            }

            decimal? modulo = null;

            if (TryConsume("%"))
            {
                modulo = ParseDecimal(Consume());
            }

            string operatorToken = Consume();

            bool equals = operatorToken switch
            {
                "=" => true,
                "!=" => false,
                _ => throw new InvalidOperationException($"Invalid CLDR operator {operatorToken}."),
            };

            List<RangeValue> ranges = [];

            do
            {
                decimal start = ParseDecimal(Consume());
                decimal end = start;

                if (TryConsume(".."))
                {
                    end = ParseDecimal(Consume());
                }

                ranges.Add(new RangeValue(start, end));
            }
            while (TryConsume(","));

            return new AndCondition(operandToken[0], modulo, equals, [.. ranges]);
        }

        private RuleExpression ParseRule()
        {
            List<AndCondition[]> orConditions = [];

            do
            {
                List<AndCondition> andConditions = [];

                do
                {
                    andConditions.Add(ParseCondition());
                }
                while (TryConsume("and"));

                orConditions.Add([.. andConditions]);
            }
            while (TryConsume("or"));

            return new RuleExpression([.. orConditions]);
        }

        private bool TryConsume(string expected)
        {
            if (_position < _tokens.Length && string.Equals(_tokens[_position], expected, StringComparison.Ordinal))
            {
                _position++;

                return true;
            }

            return false;
        }
    }

    private readonly record struct AndCondition(char Operand, decimal? Modulo, bool IsEqual, RangeValue[] Ranges)
    {
        internal bool UsesCompactOperand => Operand is 'c' or 'e';

        internal bool Evaluate(PluralOperands operands)
        {
            decimal value = GetOperandValue(operands, Operand);

            if (Modulo.HasValue)
            {
                value %= Modulo.Value;
            }

            bool inRange = false;

            foreach (RangeValue range in Ranges)
            {
                if (range.Contains(value))
                {
                    inRange = true;

                    break;
                }
            }

            return IsEqual ? inRange : !inRange;
        }

        private static decimal GetOperandValue(PluralOperands operands, char operand) =>
            operand switch
            {
                'n' => operands.N,
                'i' => (decimal)operands.I,
                'v' => operands.V,
                'w' => operands.W,
                'f' => (decimal)operands.F,
                't' => (decimal)operands.T,
                'c' => operands.C,
                'e' => operands.E,
                _ => throw new InvalidOperationException($"Unknown CLDR operand {operand}."),
            };
    }

    private readonly record struct RangeValue(decimal Start, decimal End)
    {
        internal bool Contains(decimal value)
        {
            if (decimal.Truncate(Start) == Start && decimal.Truncate(End) == End && decimal.Truncate(value) != value)
            {
                return false;
            }

            return value >= Start && value <= End;
        }
    }
}
