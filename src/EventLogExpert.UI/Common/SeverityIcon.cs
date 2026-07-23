// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.UI.Common;

internal static class SeverityIcon
{
    public static string CssClass(SeverityLevel? severity) => severity switch
    {
        SeverityLevel.Critical => "bi bi-exclamation-octagon-fill critical",
        SeverityLevel.Error => "bi bi-exclamation-circle error",
        SeverityLevel.Warning => "bi bi-exclamation-triangle warning",
        SeverityLevel.Information => "bi bi-info-circle",
        SeverityLevel.Verbose => "bi bi-circle verbose",
        _ => string.Empty
    };
}
