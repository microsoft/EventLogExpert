// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Structured;
using EventLogExpert.Filtering.Lowering;

namespace EventLogExpert.Filtering.Emit;

/// <summary>
///     Column-only scalar comparison appliers reproducing the row emitter's per-field null semantics against
///     <see cref="IEventColumnReader.GetField" />. The operator is switched once at emit time (throwing
///     <see cref="EmitException" /> for an unsupported operator exactly where the row emitter does) so the returned
///     per-event delegate branches only on field presence. Column-only by design: the row backend keeps its specialized
///     typed hot closures untouched to avoid a live-path regression.
/// </summary>
internal static class FilterCompare
{
    public static Func<IEventColumnReader, EventLocator, FilterMatch> Int64(
        EventFieldId field,
        FilterBinaryOperator op,
        long value) =>
        op switch
        {
            FilterBinaryOperator.Equal => (reader, locator) => Lift(ReadInt64(reader, locator, field) == value),
            FilterBinaryOperator.NotEqual => (reader, locator) => Lift(ReadInt64(reader, locator, field) != value),
            FilterBinaryOperator.GreaterThan => (reader, locator) => Lift(ReadInt64(reader, locator, field) > value),
            FilterBinaryOperator.LessThan => (reader, locator) => Lift(ReadInt64(reader, locator, field) < value),
            FilterBinaryOperator.GreaterThanOrEqual => (reader, locator) => Lift(ReadInt64(reader, locator, field) >= value),
            FilterBinaryOperator.LessThanOrEqual => (reader, locator) => Lift(ReadInt64(reader, locator, field) <= value),
            _ => throw new EmitException($"Unsupported integer operator '{op}'.")
        };

    public static Func<IEventColumnReader, EventLocator, FilterMatch> NullableGuid(
        EventFieldId field,
        FilterBinaryOperator op,
        Guid value) =>
        op switch
        {
            FilterBinaryOperator.Equal => (reader, locator) =>
                Lift(TryReadGuid(reader, locator, field, out var actual) && actual == value),
            FilterBinaryOperator.NotEqual => (reader, locator) =>
                Lift(!TryReadGuid(reader, locator, field, out var actual) || actual != value),
            _ => throw new EmitException($"Operator '{op}' is not supported on Guid.")
        };

    public static Func<IEventColumnReader, EventLocator, FilterMatch> NullableInt64(
        EventFieldId field,
        FilterBinaryOperator op,
        long value) =>
        op switch
        {
            FilterBinaryOperator.Equal => (reader, locator) =>
                Lift(TryReadInt64(reader, locator, field, out var actual) && actual == value),
            FilterBinaryOperator.NotEqual => (reader, locator) =>
                Lift(!TryReadInt64(reader, locator, field, out var actual) || actual != value),
            FilterBinaryOperator.GreaterThan => (reader, locator) =>
                Lift(TryReadInt64(reader, locator, field, out var actual) && actual > value),
            FilterBinaryOperator.LessThan => (reader, locator) =>
                Lift(TryReadInt64(reader, locator, field, out var actual) && actual < value),
            FilterBinaryOperator.GreaterThanOrEqual => (reader, locator) =>
                Lift(TryReadInt64(reader, locator, field, out var actual) && actual >= value),
            FilterBinaryOperator.LessThanOrEqual => (reader, locator) =>
                Lift(TryReadInt64(reader, locator, field, out var actual) && actual <= value),
            _ => throw new EmitException($"Unsupported integer operator '{op}'.")
        };

    public static Func<IEventColumnReader, EventLocator, FilterMatch> StringOrdinal(
        EventFieldId field,
        FilterBinaryOperator op,
        string value) =>
        op switch
        {
            FilterBinaryOperator.Equal => (reader, locator) =>
                Lift(string.Equals(reader.GetField(locator, field).AsString(), value, StringComparison.Ordinal)),
            FilterBinaryOperator.NotEqual => (reader, locator) =>
                Lift(!string.Equals(reader.GetField(locator, field).AsString(), value, StringComparison.Ordinal)),
            _ => throw new EmitException($"Operator '{op}' is not supported on string-form comparison.")
        };

    public static Func<IEventColumnReader, EventLocator, FilterMatch> UserIdString(
        FilterBinaryOperator op,
        string value) =>
        op switch
        {
            FilterBinaryOperator.Equal => (reader, locator) =>
            {
                var field = reader.GetField(locator, EventFieldId.UserId);

                return field.Kind == EventFieldValueKind.Null
                    ? FilterMatch.NoMatch
                    : Lift(string.Equals(field.AsString(), value, StringComparison.Ordinal));
            },
            FilterBinaryOperator.NotEqual => (reader, locator) =>
            {
                var field = reader.GetField(locator, EventFieldId.UserId);

                return field.Kind == EventFieldValueKind.Null
                    ? FilterMatch.NoMatch
                    : Lift(!string.Equals(field.AsString(), value, StringComparison.Ordinal));
            },
            _ => throw new EmitException($"Operator '{op}' is not supported on UserId.")
        };

    private static FilterMatch Lift(bool matched) => matched ? FilterMatch.Match : FilterMatch.NoMatch;

    private static long ReadInt64(IEventColumnReader reader, EventLocator locator, EventFieldId field)
    {
        reader.GetField(locator, field).TryGetInt64(out var value);

        return value;
    }

    private static bool TryReadGuid(IEventColumnReader reader, EventLocator locator, EventFieldId field, out Guid value)
    {
        var fieldValue = reader.GetField(locator, field);

        if (fieldValue.Kind == EventFieldValueKind.Null) { value = Guid.Empty; return false; }

        fieldValue.TryGetGuid(out value);

        return true;
    }

    private static bool TryReadInt64(IEventColumnReader reader, EventLocator locator, EventFieldId field, out long value)
    {
        var fieldValue = reader.GetField(locator, field);

        if (fieldValue.Kind == EventFieldValueKind.Null) { value = 0; return false; }

        fieldValue.TryGetInt64(out value);

        return true;
    }
}
