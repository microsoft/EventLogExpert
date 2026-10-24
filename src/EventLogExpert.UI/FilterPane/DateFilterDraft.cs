// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Display;

namespace EventLogExpert.UI.FilterPane;

/// <summary>Where a draft date bound's current value came from.</summary>
internal enum DateFilterBoundOrigin
{
    /// <summary>Copied from the applied range; follows the applied range until the user changes it.</summary>
    Applied,

    /// <summary>Supplied as an exact instant by a quick range the user picked.</summary>
    Preset,

    /// <summary>
    ///     Suggested by Add Date Filter while no range is applied; replaced by the applied range as soon as one appears
    ///     (e.g. from Exclude Events Before), since the user has not chosen it.
    /// </summary>
    Suggested,

    /// <summary>Typed or cleared by the user; converted from its wall-clock reading on apply.</summary>
    Edited
}

/// <summary>
///     Editable wall-clock bounds of the pane date filter, in the display time zone. Mutable and long-lived so the
///     <see cref="Microsoft.AspNetCore.Components.Forms.EditContext" /> built over it stays bound to the live values.
///     Bounds loaded from an instant keep that exact instant until the user edits them, so applying an untouched bound
///     never loses sub-second precision or resolves a repeated daylight-saving reading to the wrong instant.
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

    /// <summary>
    ///     After a display zone change, preset and suggested bounds keep their reading and are reinterpreted in the new
    ///     zone like a typed value, matching the wall-clock semantics of the applied range. A preset becomes an edit; a
    ///     suggestion stays a suggestion so an applied range appearing later still replaces it.
    /// </summary>
    public void ReleasePresetInstants()
    {
        if (AfterOrigin == DateFilterBoundOrigin.Preset) { After = _after; }
        else if (AfterOrigin == DateFilterBoundOrigin.Suggested) { AfterExactUtc = null; }

        if (BeforeOrigin == DateFilterBoundOrigin.Preset) { Before = _before; }
        else if (BeforeOrigin == DateFilterBoundOrigin.Suggested) { BeforeExactUtc = null; }
    }
}
