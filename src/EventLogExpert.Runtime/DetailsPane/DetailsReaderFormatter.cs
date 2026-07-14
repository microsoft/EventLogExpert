// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Structured;
using EventLogExpert.Runtime.Common.Display;
using System.Globalization;
using System.Text;

namespace EventLogExpert.Runtime.DetailsPane;

/// <summary>
///     Projects a <see cref="ResolvedEvent" /> into the <see cref="DetailsReaderModel" /> the details pane renders,
///     and builds the clipboard text for the whole event and for each section. All display strings are pre-rendered here
///     so the component retains no transient <see cref="EventFieldValue" />, and the copy builders reuse the projection so
///     displayed and copied text cannot drift. EventData fields render their names (or a positional <c>[i]</c> when
///     unnamed) with one line per array item, a hex preview for binary values, muted placeholders for empty values, and
///     any inline explanation from <see cref="EventFieldExplainer" />; UserData fields are never run through the explainer
///     because their leaf paths lack the provider / event context the glossary keys on.
/// </summary>
public static class DetailsReaderFormatter
{
    private const int BytesPreviewByteCount = 64;
    private const string EmptyArrayPlaceholder = "(no values)";
    private const string EmptyStringPlaceholder = "(empty)";
    private const int LongTextPreviewLength = 500;
    private const string NullValuePlaceholder = "(none)";

    public static string BuildEventCopyText(DetailsReaderModel model)
    {
        StringBuilder builder = new();

        builder.AppendLine($"Event ID: {model.EventId}");

        if (!string.IsNullOrEmpty(model.Level)) { builder.AppendLine($"Level: {model.Level}"); }

        foreach (DetailsProperty property in model.Header)
        {
            builder.AppendLine($"{property.Label}: {property.Value}");
        }

        foreach (DetailsProperty property in model.SystemProperties)
        {
            builder.AppendLine($"{property.Label}: {property.Value}");
        }

        if (model.HasMessage)
        {
            builder.AppendLine();
            builder.AppendLine("Message:");
            builder.AppendLine(model.Message);
        }

        AppendFieldsSection(builder, "Event Data:", model.EventData);
        AppendFieldsSection(builder, "User Data:", model.UserData);

        return builder.ToString().TrimEnd();
    }

    public static string BuildFieldsCopyText(IReadOnlyList<DetailsField> fields)
    {
        StringBuilder builder = new();

        foreach (DetailsField field in fields)
        {
            AppendFieldCopyText(builder, field);
        }

        return builder.ToString().TrimEnd();
    }

    public static DetailsReaderModel BuildModel(ResolvedEvent @event, TimeZoneInfo timeZone) =>
        new()
        {
            EventId = @event.Id.ToString(CultureInfo.InvariantCulture),
            Level = @event.Level,
            Severity = LevelSeverity.FromLevelName(@event.Level),
            Header = BuildHeader(@event, timeZone),
            SystemProperties = BuildSystemProperties(@event),
            EventData = BuildEventDataFields(@event, timeZone),
            UserData = BuildUserDataFields(@event),
            Message = @event.Description,
            HasMessage = !string.IsNullOrEmpty(@event.Description),
            UserDataIncomplete = @event.UserDataIncomplete
        };

    public static string BuildPropertiesCopyText(IReadOnlyList<DetailsProperty> properties)
    {
        StringBuilder builder = new();

        foreach (DetailsProperty property in properties)
        {
            builder.AppendLine($"{property.Label}: {property.Value}");
        }

        return builder.ToString().TrimEnd();
    }

    private static void AddIfPresent(List<DetailsProperty> properties, string label, string value)
    {
        if (!string.IsNullOrEmpty(value)) { properties.Add(new DetailsProperty(label, value)); }
    }

    private static void AppendFieldCopyText(StringBuilder builder, DetailsField field)
    {
        if (field.CopyValue.Contains('\n'))
        {
            builder.AppendLine($"{field.Label}:");

            foreach (string line in field.CopyValue.Split('\n'))
            {
                builder.AppendLine($"    {line}");
            }

            return;
        }

        builder.Append(field.Label).Append(": ").Append(field.CopyValue);

        if (field.DecodedLabel is { } decoded)
        {
            builder.Append(" (").Append(decoded).Append(')');
        }

        builder.AppendLine();
    }

    private static void AppendFieldsSection(StringBuilder builder, string heading, IReadOnlyList<DetailsField> fields)
    {
        if (fields.Count == 0) { return; }

        builder.AppendLine();
        builder.AppendLine(heading);

        foreach (DetailsField field in fields)
        {
            AppendFieldCopyText(builder, field);
        }
    }

    private static IReadOnlyList<DetailsField> BuildEventDataFields(ResolvedEvent @event, TimeZoneInfo timeZone)
    {
        List<DetailsField> fields = [];
        int index = 0;

        foreach (EventDataView.Field field in @event.EventData)
        {
            string label = string.IsNullOrEmpty(field.Name)
                ? $"[{index}]"
                : field.Name;

            EventFieldExplanation explanation =
                EventFieldExplainer.TryExplain(@event.Source, @event.Id, field.Name, field.Value, out EventFieldExplanation resolved)
                    ? resolved
                    : default;

            fields.Add(ToField(label, RenderValue(field.Value, timeZone), explanation));
            index++;
        }

        return fields;
    }

    private static IReadOnlyList<DetailsProperty> BuildHeader(ResolvedEvent @event, TimeZoneInfo timeZone)
    {
        // Event ID + Level are projected as typed summary fields (with the severity bucket) and rendered separately, so
        // they are intentionally not in this list; BuildEventCopyText re-emits them first to keep the clipboard order.
        List<DetailsProperty> properties = [];

        AddIfPresent(properties, "Source", @event.Source);
        properties.Add(new DetailsProperty("Date and Time", @event.TimeCreated.ConvertTimeZone(timeZone).ToString(CultureInfo.CurrentCulture)));
        AddIfPresent(properties, "Computer", @event.ComputerName);
        AddIfPresent(properties, "Log Name", @event.LogName);

        return properties;
    }

    private static IReadOnlyList<DetailsProperty> BuildSystemProperties(ResolvedEvent @event)
    {
        List<DetailsProperty> properties = [];

        AddIfPresent(properties, "Task Category", @event.TaskCategory);
        AddIfPresent(properties, "Opcode", @event.Opcode);
        AddIfPresent(properties, "Keywords", @event.KeywordsDisplayName);

        if (@event.RecordId is { } recordId) { properties.Add(new DetailsProperty("Record ID", recordId.ToString(CultureInfo.InvariantCulture))); }

        if (@event.ProcessId is { } processId) { properties.Add(new DetailsProperty("Process ID", processId.ToString(CultureInfo.InvariantCulture))); }

        if (@event.ThreadId is { } threadId) { properties.Add(new DetailsProperty("Thread ID", threadId.ToString(CultureInfo.InvariantCulture))); }

        if (@event.ActivityId is { } activityId) { properties.Add(new DetailsProperty("Activity ID", activityId.ToString())); }

        if (@event.RelatedActivityId is { } relatedActivityId) { properties.Add(new DetailsProperty("Related Activity ID", relatedActivityId.ToString())); }

        // Raw SID by design; account-name resolution is a separate task.
        if (@event.UserId is { } userId) { properties.Add(new DetailsProperty("User", userId.Value)); }

        return properties;
    }

    private static IReadOnlyList<DetailsField> BuildUserDataFields(ResolvedEvent @event)
    {
        if (@event.UserData.IsDefaultOrEmpty) { return []; }

        List<DetailsField> fields = [];

        foreach (UserDataField field in @event.UserData)
        {
            string[] values = field.Values.IsDefaultOrEmpty ? [] : [.. field.Values];

            fields.Add(ToField(field.Path, RenderArray(values), default));
        }

        return fields;
    }

    private static string GroupHex(byte[] bytes, int count)
    {
        StringBuilder builder = new(count * 3);

        for (int i = 0; i < count; i++)
        {
            if (i > 0) { builder.Append(' '); }

            builder.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static RenderedValue Placeholder(string text)
    {
        string[] lines = [text];

        return new RenderedValue(lines, lines, string.Empty, IsTruncated: false, IsMuted: true);
    }

    private static RenderedValue RenderArray(string[] items) =>
        items.Length == 0 ?
            Placeholder(EmptyArrayPlaceholder) :
            new RenderedValue(items, items, string.Join('\n', items), IsTruncated: false, IsMuted: false);

    private static RenderedValue RenderBytes(byte[] bytes)
    {
        if (bytes.Length == 0) { return Placeholder(EmptyStringPlaceholder); }

        string copyValue = Convert.ToHexString(bytes);
        string full = GroupHex(bytes, bytes.Length);

        if (bytes.Length > BytesPreviewByteCount)
        {
            return new RenderedValue([$"{GroupHex(bytes, BytesPreviewByteCount)} ..."], [full], copyValue, IsTruncated: true, IsMuted: false, IsMonospace: true);
        }

        string[] lines = [full];

        return new RenderedValue(lines, lines, copyValue, IsTruncated: false, IsMuted: false, IsMonospace: true);
    }

    private static RenderedValue RenderScalarText(string text)
    {
        if (text.Length == 0) { return Placeholder(EmptyStringPlaceholder); }

        if (text.Length > LongTextPreviewLength)
        {
            return new RenderedValue([$"{text[..LongTextPreviewLength]}..."], [text], text, IsTruncated: true, IsMuted: false);
        }

        string[] lines = [text];

        return new RenderedValue(lines, lines, text, IsTruncated: false, IsMuted: false);
    }

    private static RenderedValue RenderValue(in EventFieldValue value, TimeZoneInfo timeZone)
    {
        if (value.TryGetStringArray(out string[]? strings)) { return RenderArray(strings); }

        if (value.TryGetArray(out Array? array)) { return RenderArray(ToStringItems(array)); }

        if (value.TryGetBytes(out byte[]? bytes)) { return RenderBytes(bytes); }

        if (value.TryGetDateTime(out DateTime timestamp))
        {
            return RenderScalarText(timestamp.ConvertTimeZone(timeZone).ToString(CultureInfo.CurrentCulture));
        }

        if (value.Kind == EventFieldValueKind.Null) { return Placeholder(NullValuePlaceholder); }

        RenderedValue scalar = RenderScalarText(value.AsString());

        // GUIDs and SIDs scan better fixed-width; general strings and numbers keep the app's sans-serif.
        return value.Kind is EventFieldValueKind.Guid or EventFieldValueKind.Sid
            ? scalar with { IsMonospace = true }
            : scalar;
    }

    private static DetailsField ToField(string label, RenderedValue rendered, EventFieldExplanation explanation) =>
        new()
        {
            Label = label,
            PreviewLines = rendered.PreviewLines,
            FullLines = rendered.FullLines,
            CopyValue = rendered.CopyValue,
            IsTruncated = rendered.IsTruncated,
            IsMuted = rendered.IsMuted,
            IsMonospace = rendered.IsMonospace,
            DecodedLabel = explanation.DecodedLabel,
            Description = explanation.Description
        };

    private static string[] ToStringItems(Array array)
    {
        string[] items = new string[array.Length];

        for (int i = 0; i < array.Length; i++)
        {
            items[i] = Convert.ToString(array.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return items;
    }

    private readonly record struct RenderedValue(
        IReadOnlyList<string> PreviewLines,
        IReadOnlyList<string> FullLines,
        string CopyValue,
        bool IsTruncated,
        bool IsMuted,
        bool IsMonospace = false);
}
