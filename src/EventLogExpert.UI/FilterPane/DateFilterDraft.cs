// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Display;

namespace EventLogExpert.UI.FilterPane;

/// <summary>
///     Editable wall-clock bounds of the pane date filter whose instant-loaded bounds keep their exact instant until
///     edited or reinterpreted in a new time zone.
/// </summary>
internal sealed class DateFilterDraft
{
    private DateTime? _after;
    private DateTime? _before;

    public DateTime? After
    {
        get => _after;
        set
        {
            _after = value;
            AfterOrigin = DateFilterBoundOrigin.Edited;
            AfterExactUtc = null;
        }
    }

    public DateTime? AfterExactUtc { get; private set; }

    public DateFilterBoundOrigin AfterOrigin { get; private set; }

    public DateTime? Before
    {
        get => _before;
        set
        {
            _before = value;
            BeforeOrigin = DateFilterBoundOrigin.Edited;
            BeforeExactUtc = null;
        }
    }

    public DateTime? BeforeExactUtc { get; private set; }

    public DateFilterBoundOrigin BeforeOrigin { get; private set; }

    public void LoadAfter(DateTime? utc, TimeZoneInfo displayZone, DateFilterBoundOrigin origin)
    {
        _after = utc?.ConvertTimeZone(displayZone);
        AfterExactUtc = utc;
        AfterOrigin = origin;
    }

    public void LoadBefore(DateTime? utc, TimeZoneInfo displayZone, DateFilterBoundOrigin origin)
    {
        _before = utc?.ConvertTimeZone(displayZone);
        BeforeExactUtc = utc;
        BeforeOrigin = origin;
    }

    public void MarkAfterEdited() => After = _after;

    public void MarkBeforeEdited() => Before = _before;

    /// <summary>
    ///     Drops preset and suggested exact instants so Apply converts their readings in the current zone; presets become
    ///     edits.
    /// </summary>
    public void ReinterpretInNewTimeZone()
    {
        switch (AfterOrigin)
        {
            case DateFilterBoundOrigin.Preset: After = _after; break;
            case DateFilterBoundOrigin.Suggested: AfterExactUtc = null; break;
        }

        switch (BeforeOrigin)
        {
            case DateFilterBoundOrigin.Preset: Before = _before; break;
            case DateFilterBoundOrigin.Suggested: BeforeExactUtc = null; break;
        }
    }
}
