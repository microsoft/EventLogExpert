// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using System.Globalization;

namespace EventLogExpert.UI.Tests.TestUtils;

/// <summary>
///     bUnit context that restores the construction-time thread <see cref="CultureInfo.CurrentCulture" /> AND
///     <see cref="CultureInfo.CurrentUICulture" /> on dispose, so a ctor's culture pin cannot leak onto a pooled thread.
/// </summary>
public abstract class CultureSensitiveBunitContext : BunitContext
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;

    protected override void Dispose(bool disposing)
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
        base.Dispose(disposing);
    }
}
