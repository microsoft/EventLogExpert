// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using System.Globalization;

namespace EventLogExpert.Filtering.Lowering;

/// <summary>
///     A filter comparison operand pre-parsed once at lower-time into a candidate value for every scalar
///     <see cref="EventFieldValueKind" />, so the per-event hot path does no parsing and no allocation for packed-scalar /
///     <see cref="Guid" /> / <see cref="EventFieldValueKind.String" /> field kinds. Implements the
///     <em>typed value equality</em> model: a numeric / bool / date / Guid field is matched by its parsed value (so
///     <c>"5"</c> and <c>"05"</c> both match a numeric <c>5</c>, and a Guid matches in any parseable format), while
///     reference/blob kinds (<see cref="EventFieldValueKind.Sid" />, <see cref="EventFieldValueKind.Bytes" />, the array
///     kinds, and <see cref="EventFieldValueKind.Null" />) fall back to an ordinal compare against
///     <see cref="EventFieldValue.AsString" />.
/// </summary>
internal sealed class EventDataLiteral
{
    private readonly bool? _boolean;
    private readonly DateTime? _dateTime;
    private readonly double? _double;
    private readonly Guid? _guid;
    private readonly long? _int64;
    private readonly string _raw;
    private readonly float? _single;
    private readonly ulong? _uint64;

    private EventDataLiteral(
        string raw,
        long? int64,
        ulong? uint64,
        double? doubleValue,
        float? single,
        bool? boolean,
        DateTime? dateTime,
        Guid? guid)
    {
        _raw = raw;
        _int64 = int64;
        _uint64 = uint64;
        _double = doubleValue;
        _single = single;
        _boolean = boolean;
        _dateTime = dateTime;
        _guid = guid;
    }

    /// <summary>The original literal text, used by the decomposer to round-trip back to a Basic value.</summary>
    public string Raw => _raw;

    /// <summary>Parses the raw literal into one candidate per scalar kind (each null when the literal is not that kind).</summary>
    public static EventDataLiteral Parse(string raw)
    {
        long? int64 = long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
        ulong? uint64 = ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u) ? u : null;
        double? doubleValue =
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
        float? single = float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : null;
        bool? boolean = bool.TryParse(raw, out var b) ? b : null;
        // Strictly the inverse of EventFieldValue.AsString's ToString("O"): only the canonical round-trip form
        // matches (picklist values are already "O"), not lenient/ambiguous date formats.
        DateTime? dateTime =
            DateTime.TryParseExact(raw, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                ? dt
                : null;
        Guid? guid = Guid.TryParse(raw, out var g) ? g : null;

        return new EventDataLiteral(raw, int64, uint64, doubleValue, single, boolean, dateTime, guid);
    }

    /// <summary>
    ///     Typed value equality against <paramref name="value" />. Zero-allocation for the packed-scalar kinds,
    ///     <see cref="Guid" />, and <see cref="EventFieldValueKind.String" />; the reference/blob and
    ///     <see cref="EventFieldValueKind.Null" /> kinds compare against <see cref="EventFieldValue.AsString" />.
    /// </summary>
    public bool MatchesValue(in EventFieldValue value) =>
        value.Kind switch
        {
            EventFieldValueKind.Int64 => _int64 is { } c && value.TryGetInt64(out var a) && a == c,
            EventFieldValueKind.UInt64 => _uint64 is { } c && value.TryGetUInt64(out var a) && a == c,
            // double.Equals / float.Equals treat NaN as equal to NaN (matching AsString "NaN"), unlike ==.
            EventFieldValueKind.Double => _double is { } c && value.TryGetDouble(out var a) && a.Equals(c),
            EventFieldValueKind.Single => _single is { } c && value.TryGetSingle(out var a) && a.Equals(c),
            EventFieldValueKind.Boolean => _boolean is { } c && value.TryGetBoolean(out var a) && a == c,
            EventFieldValueKind.DateTime => _dateTime is { } c && value.TryGetDateTime(out var a) && a == c,
            EventFieldValueKind.Guid => _guid is { } c && value.TryGetGuid(out var a) && a == c,
            EventFieldValueKind.String => string.Equals(value.AsString(), _raw, StringComparison.Ordinal),
            _ => string.Equals(value.AsString(), _raw, StringComparison.Ordinal)
        };
}
