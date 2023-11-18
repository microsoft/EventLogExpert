// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using System.Text;

namespace EventLogExpert.UI.Services;

public interface IAppTitleService
{
    void SetIsPrerelease(bool isPrerelease);

    void SetLogName(string? logName);

    void SetProgressString(string? progressString);
}

public sealed class AppTitleService(
    ICurrentVersionProvider versionProvider,
    ITitleProvider titleProvider) : IAppTitleService
{
    private bool _isPrereleaseBuild = false;

    private string? _logName;

    private string? _progressString;

    public void SetIsPrerelease(bool isPrerelease) => _isPrereleaseBuild = isPrerelease;

    public void SetLogName(string? logName)
    {
        _logName = logName;
        SetTitle();
    }

    public void SetProgressString(string? progressString)
    {
        _progressString = progressString;
        SetTitle();
    }

    private void SetTitle()
    {
        StringBuilder title = new();

        if (_progressString is not null)
        {
            title.Append($"{_progressString} - ");
        }

        if (_logName is not null)
        {
            title.Append($"{_logName} - ");
        }

        title.Append("EventLogExpert ");

        if (versionProvider.IsDevBuild)
        {
            title.Append("(Development)");
        }
        else if (_isPrereleaseBuild)
        {
            title.Append($"(Preview) {versionProvider.CurrentVersion}");
        }
        else
        {
            title.Append(versionProvider.CurrentVersion);
        }

        titleProvider.SetTitle(title.ToString());
    }
}
