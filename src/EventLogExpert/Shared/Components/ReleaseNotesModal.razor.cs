// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using EventLogExpert.UI.Services;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components;

public sealed partial class ReleaseNotesModal : ModalBase<bool>
{
    private string _html = string.Empty;

    [EditorRequired]
    [Parameter] public ReleaseNotesContent Content { get; set; }

    protected override void OnParametersSet()
    {
        // ReleaseNotesContent is a struct; defend against a missing parameter (default(struct))
        // even though [EditorRequired] surfaces the omission as a build warning.
        _html = ReleaseNotesMarkdownRenderer.RenderToHtml(Content.Title ?? string.Empty, Content.Markdown ?? string.Empty);

        base.OnParametersSet();
    }
}
