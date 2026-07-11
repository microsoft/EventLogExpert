// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Common.Filtering;

public static class FilterPropertyConstraints
{
    public static bool IsTextOnly(EventProperty property) =>
        property is EventProperty.Description or EventProperty.Xml;

    public static bool SupportsMany(EventProperty property) => !IsTextOnly(property);

    /// <summary>
    ///     True for the scalar string properties whose multi-select honors the operator (Contains-any, Is-none-of,
    ///     Contains-none) in addition to the default Equals-any. Numeric/Guid, Keywords, and EventData/UserData support only
    ///     Equals-any multi-select; text-only fields support no multi-select.
    /// </summary>
    public static bool SupportsManyOperators(EventProperty property) =>
        property is EventProperty.Source
            or EventProperty.Level
            or EventProperty.LogName
            or EventProperty.TaskCategory
            or EventProperty.UserId
            or EventProperty.Opcode;
}
