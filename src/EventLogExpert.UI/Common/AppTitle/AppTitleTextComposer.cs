// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.Common.AppTitle;
using Microsoft.Extensions.Localization;
using System.Text;

namespace EventLogExpert.UI.Common.AppTitle;

public sealed class AppTitleTextComposer(
    IStringLocalizer<SharedResource> localizer,
    ITitleProvider titleProvider) : IAppTitleTextComposer
{
    public void Compose(AppTitleState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        StringBuilder title = new();

        if (state.Progress is { } progress)
        {
            var progressText = FormatProgress(progress);

            if (!string.IsNullOrEmpty(progressText))
            {
                title.Append(progressText).Append(" - ");
            }
        }

        title.Append("EventLogExpert");

        if (state.IsDevBuild)
        {
            title.Append(localizer["AppTitle_Qualifier_Development"].Value);
        }
        else if (state.IsPrerelease)
        {
            title.Append(localizer["AppTitle_Qualifier_Preview"].Value).Append(' ').Append(state.Version);
        }
        else
        {
            title.Append(' ').Append(state.Version);
        }

        if (state.IsAdmin)
        {
            title.Append(localizer["AppTitle_Qualifier_Admin"].Value);
        }

        if (state.LogName is not null)
        {
            title.Append(" - ").Append(state.LogName);
        }

        titleProvider.SetTitle(title.ToString());
    }

    private string FormatProgress(AppTitleProgress progress) => progress switch
    {
        AppTitleProgress.Installing installing => localizer["AppTitle_Progress_Installing", installing.Percent].Value,
        AppTitleProgress.RelaunchToApply => localizer["AppTitle_Progress_Relaunch"].Value,
        _ => string.Empty
    };
}
