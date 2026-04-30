// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Services;

/// <summary>
///     Controls how an <see cref="IAlertDialogService.ShowAlert(string,string,string,AlertPresentation)" /> request is
///     surfaced to the user.
/// </summary>
public enum AlertPresentation
{
    /// <summary>
    ///     Default. Use the existing <see cref="ModalAlertDialogService" /> routing: render inline in the active modal host
    ///     if one is registered, otherwise open a standalone alert popup.
    /// </summary>
    Auto,

    /// <summary>
    ///     Route to <see cref="IBannerService.ReportInfoBanner" /> with <see cref="BannerSeverity.Warning" /> severity. Only
    ///     valid for one-button overloads (the banner has no accept/cancel pair); using it on a two-button overload throws.
    /// </summary>
    Banner,

    /// <summary>
    ///     Require an active inline alert host. Throws <see cref="InvalidOperationException" /> if none is registered.
    /// </summary>
    InlineOnly,

    /// <summary>
    ///     Always open a standalone popup, even if an inline host is registered.
    /// </summary>
    PopupOnly,
}
