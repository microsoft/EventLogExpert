// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.WindowsPlatform.Dialogs;

/// <summary>
///     The localized display strings and the extension pattern the Win32 open/save filter buffer needs, all resolved
///     and formatted on the UI thread before they cross into the native P/Invoke layer (which does no culture-sensitive
///     work). Build instances with <see cref="Win32FileDialog.CreateFilterChrome" />, which computes the extension
///     <see cref="Pattern" /> ONCE, formats the "Supported types" label with it, applies the English fallback, and clamps
///     both labels to <see cref="Win32FileDialog.MaxFilterLabelChars" />. Capturing the pattern here (rather than
///     recomputing it in <see cref="Win32FileDialog.BuildFilter" />) guarantees the display label and the native filter
///     token never drift and that the buffer's measure and write passes are identical regardless of the source list's
///     mutability.
/// </summary>
/// <param name="SupportedTypesLabel">The finished "Supported types (*.evtx)" label shown for the app's own file types.</param>
/// <param name="AllFilesLabel">The finished "All files" label shown for the wildcard (<c>*.*</c>) filter row.</param>
/// <param name="Pattern">The <c>"*.evtx;*.etl"</c> extension token written to the native filter buffer.</param>
public readonly record struct FilterChrome(string SupportedTypesLabel, string AllFilesLabel, string Pattern);
