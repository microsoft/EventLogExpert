// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Components.Base;
using EventLogExpert.UI.Common.Markdown;
using EventLogExpert.UI.Update.ReleaseNotes;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Components.Modals;

public sealed partial class ReleaseNotesModal : ModalBase<bool>
{
    private string _html = string.Empty;

    [EditorRequired]
    [Parameter] public ReleaseNotesContent Content { get; set; }

    protected override void OnParametersSet()
    {
        // ReleaseNotesContent is a struct; defend against a missing parameter (default(struct))
        // even though [EditorRequired] surfaces the omission as a build warning.
        _html = MarkdownRenderer.RenderToHtml(Content.Title ?? string.Empty, Content.Markdown ?? string.Empty);

        base.OnParametersSet();
    }
}
