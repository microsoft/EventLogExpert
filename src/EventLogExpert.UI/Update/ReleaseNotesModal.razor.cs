// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Common.Markdown;
using EventLogExpert.Runtime.Update.ReleaseNotes;
using EventLogExpert.UI.Modal;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Update;

public sealed partial class ReleaseNotesModal : ModalBase<bool>
{
    private string _html = string.Empty;

    [EditorRequired]
    [Parameter] public ReleaseNotesContent Content { get; set; }

    [Inject] private IStringLocalizer<SharedResource> Localizer { get; init; } = null!;

    protected override void OnParametersSet()
    {
        // ReleaseNotesContent is a struct; defend against a missing parameter (default(struct))
        // even though [EditorRequired] surfaces the omission as a build warning. Version is data; the
        // heading is composed + localized here (Runtime stays localizer-free).
        var title = Localizer["ReleaseNotes_TitleWithVersion", Content.Version ?? string.Empty].Value;
        _html = MarkdownRenderer.RenderToHtml(title, Content.Markdown ?? string.Empty);

        base.OnParametersSet();
    }
}
