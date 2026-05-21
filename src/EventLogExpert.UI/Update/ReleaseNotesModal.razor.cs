// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Modal;
using EventLogExpert.Runtime.Common.Markdown;
using EventLogExpert.Runtime.Update.ReleaseNotes;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.Update;

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
