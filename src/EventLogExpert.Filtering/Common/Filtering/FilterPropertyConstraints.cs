// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Common.Filtering;

public static class FilterPropertyConstraints
{
    public static bool IsTextOnly(EventProperty property) =>
        property is EventProperty.Description or EventProperty.Xml;

    /// <summary>
    ///     True for fields whose multi-select offers "Contains Any": the scalar string fields plus the dynamic
    ///     EventData/UserData fields (positive-only there). Numeric/Guid, Keywords, and text-only fields do not.
    /// </summary>
    public static bool SupportsContainsMany(EventProperty property) =>
        IsScalarStringManyField(property) || property is EventProperty.EventData or EventProperty.UserData;

    public static bool SupportsMany(EventProperty property) => !IsTextOnly(property);

    /// <summary>
    ///     True for fields whose multi-select offers the negated "Is None Of" / "Contains None" kinds. Limited to the
    ///     scalar string fields (EventData/UserData any-of negation is presence-required/tri-state and unsupported).
    /// </summary>
    public static bool SupportsNoneOfMany(EventProperty property) =>
        IsScalarStringManyField(property);

    /// <summary>
    ///     The scalar string properties whose multi-select supports the full operator set (Contains-any and the negated
    ///     Is-none-of / Contains-none). EventData/UserData and everything else are handled separately below.
    /// </summary>
    private static bool IsScalarStringManyField(EventProperty property) =>
        property is EventProperty.Source
            or EventProperty.Level
            or EventProperty.LogName
            or EventProperty.TaskCategory
            or EventProperty.UserId
            or EventProperty.Opcode;
}
