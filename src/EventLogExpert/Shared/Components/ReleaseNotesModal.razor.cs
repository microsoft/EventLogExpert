// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Shared.Base;
using EventLogExpert.UI.Services;
using Microsoft.AspNetCore.Components;

namespace EventLogExpert.Shared.Components;

public sealed partial class ReleaseNotesModal : BaseModal<bool>
{
    private string _html = string.Empty;

    [Parameter] public ReleaseNotesContent Content { get; set; }

    protected override void OnParametersSet()
    {
        _html = ReleaseNotesMarkdownRenderer.RenderToHtml(Content.Title, Content.Markdown);
        base.OnParametersSet();
    }
}
