// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Filtering.Lowering;

/// <summary>
///     Case-insensitive identifier-to-<see cref="ResolvedEventField" /> dispatch. Hardcoded switch (no reflection)
///     for two reasons: (1) zero allocation on the hot validation path; (2) AOT-friendly. A schema-sync unit test reflects
///     over <see cref="ResolvedEvent" /> at test-time and asserts every public property is resolvable here.
/// </summary>
internal static class PropertyResolver
{
    /// <summary>
    ///     Returns the <see cref="ResolvedEventField" /> matching <paramref name="identifier" /> (case-insensitive), plus
    ///     the literal kind a comparison literal must coerce to.
    /// </summary>
    public static bool TryResolve(string identifier, out ResolvedEventField field, out TypedLiteralKind literalKind)
    {
        field = default;
        literalKind = default;

        if (identifier is null) { return false; }

        switch (identifier.ToUpperInvariant())
        {
            case "ACTIVITYID":
                field = ResolvedEventField.ActivityId;
                literalKind = TypedLiteralKind.Guid;

                return true;
            case "COMPUTERNAME":
                field = ResolvedEventField.ComputerName;
                literalKind = TypedLiteralKind.String;

                return true;
            case "DESCRIPTION":
                field = ResolvedEventField.Description;
                literalKind = TypedLiteralKind.String;

                return true;
            case "ID":
                field = ResolvedEventField.Id;
                literalKind = TypedLiteralKind.Int;

                return true;
            case "KEYWORDS":
                field = ResolvedEventField.Keywords;
                literalKind = TypedLiteralKind.String;

                return true;
            case "LEVEL":
                field = ResolvedEventField.Level;
                literalKind = TypedLiteralKind.String;

                return true;
            case "LOGNAME":
                field = ResolvedEventField.LogName;
                literalKind = TypedLiteralKind.String;

                return true;
            case "PROCESSID":
                field = ResolvedEventField.ProcessId;
                literalKind = TypedLiteralKind.Int;

                return true;
            case "RECORDID":
                field = ResolvedEventField.RecordId;
                literalKind = TypedLiteralKind.Long;

                return true;
            case "SOURCE":
                field = ResolvedEventField.Source;
                literalKind = TypedLiteralKind.String;

                return true;
            case "TASKCATEGORY":
                field = ResolvedEventField.TaskCategory;
                literalKind = TypedLiteralKind.String;

                return true;
            case "THREADID":
                field = ResolvedEventField.ThreadId;
                literalKind = TypedLiteralKind.Int;

                return true;
            case "TIMECREATED":
                field = ResolvedEventField.TimeCreated;
                literalKind = TypedLiteralKind.String;

                return true;
            case "USERID":
                field = ResolvedEventField.UserId;
                literalKind = TypedLiteralKind.String;

                return true;
            case "XML":
                field = ResolvedEventField.Xml;
                literalKind = TypedLiteralKind.String;

                return true;
            default:
                return false;
        }
    }
}
