// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering.Common.Filtering;

public static class FilterPropertyConstraints
{
    public static bool IsTextOnly(EventProperty property) =>
        property is EventProperty.Description or EventProperty.Xml;

    public static bool SupportsMany(EventProperty property) => !IsTextOnly(property);
}
