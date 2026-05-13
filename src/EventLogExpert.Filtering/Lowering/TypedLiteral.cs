// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Globalization;

namespace EventLogExpert.Filtering.Lowering;

/// <summary>
///     A literal whose target type was resolved at lower-time via the property under comparison. Storing the parsed
///     value once means the per-event hot path performs a raw typed compare with no per-event allocation or coercion.
/// </summary>
internal readonly struct TypedLiteral
{
    private TypedLiteral(
        TypedLiteralKind kind,
        string? stringValue,
        int intValue,
        long longValue,
        Guid guidValue)
    {
        Kind = kind;
        StringValue = stringValue;
        IntValue = intValue;
        LongValue = longValue;
        GuidValue = guidValue;
    }

    public TypedLiteralKind Kind { get; }

    public string? StringValue { get; }

    public int IntValue { get; }

    public long LongValue { get; }

    public Guid GuidValue { get; }

    public static TypedLiteral Null { get; } =
        new(TypedLiteralKind.Null, null, default, default, default);

    public static TypedLiteral String(string value) =>
        new(TypedLiteralKind.String, value, default, default, default);

    public static TypedLiteral Int(int value) =>
        new(TypedLiteralKind.Int, null, value, default, default);

    public static TypedLiteral Long(long value) =>
        new(TypedLiteralKind.Long, null, default, value, default);

    public static TypedLiteral Guid(Guid value) =>
        new(TypedLiteralKind.Guid, null, default, default, value);

    /// <summary>
    ///     Attempts to coerce <paramref name="raw" /> (a raw string from the source) into a typed literal that matches
    ///     <paramref name="targetKind" />. Returns <c>false</c> on coercion failure so the caller can decide whether to
    ///     surface a diagnostic or compile to always-false (per N-D6 parity rules).
    /// </summary>
    public static bool TryCoerce(string raw, TypedLiteralKind targetKind, out TypedLiteral literal)
    {
        switch (targetKind)
        {
            case TypedLiteralKind.String:
                literal = String(raw);

                return true;
            case TypedLiteralKind.Int:
                if (int.TryParse(
                        raw.AsSpan().Trim(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var intValue))
                {
                    literal = Int(intValue);

                    return true;
                }

                break;
            case TypedLiteralKind.Long:
                if (long.TryParse(
                        raw.AsSpan().Trim(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var longValue))
                {
                    literal = Long(longValue);

                    return true;
                }

                break;
            case TypedLiteralKind.Guid:
                if (System.Guid.TryParse(raw.AsSpan().Trim(), out var guidValue))
                {
                    literal = Guid(guidValue);

                    return true;
                }

                break;
            case TypedLiteralKind.Null:
                literal = Null;

                return true;
        }

        literal = default;

        return false;
    }
}
