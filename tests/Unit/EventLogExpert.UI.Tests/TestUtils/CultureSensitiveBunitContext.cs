// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Bunit;
using System.Globalization;

namespace EventLogExpert.UI.Tests.TestUtils;

/// <summary>
///     bUnit context that restores the construction-time thread culture on dispose, so a ctor's
///     <see cref="CultureInfo.InvariantCulture" /> pin cannot leak onto a pooled thread.
/// </summary>
public abstract class CultureSensitiveBunitContext : BunitContext
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;

    protected override void Dispose(bool disposing)
    {
        CultureInfo.CurrentCulture = _originalCulture;
        base.Dispose(disposing);
    }
}
