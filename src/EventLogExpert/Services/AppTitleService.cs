// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text;

namespace EventLogExpert.Services;

public interface IAppTitleService
{
    void SetIsPrerelease(bool isPrerelease);

    void SetLogName(string? logName);

    void SetProgressString(string? progressString);
}

public class AppTitleService : IAppTitleService
{
    private readonly ICurrentVersionProvider _versionProvider;

    private bool _isPrereleaseBuild = false;

    private string? _logName;

    private string? _progressString;

    public AppTitleService(ICurrentVersionProvider versionProvider)
    {
        _versionProvider = versionProvider;
    }

    public void SetIsPrerelease(bool isPrerelease) => _isPrereleaseBuild = isPrerelease;

    public void SetLogName(string? logName)
    {
        _logName = logName;
        SetTitle();
    }

    public void SetProgressString( string? progressString)
    {
        _progressString = progressString;
        SetTitle();
    }

    private void SetTitle()
    {
        if (Application.Current?.Windows.Any() is not true) { return; }

        StringBuilder title = new();

        if (_progressString is not null)
        {
            title.Append(_progressString);
        }

        if (_logName is not null)
        {
            title.Append($"{_logName} - ");
        }

        title.Append("EventLogExpert ");

        if (_versionProvider.IsDevBuild)
        {
            title.Append("(Development)");
        }
        else if (_isPrereleaseBuild)
        {
            title.Append($"(Preview) {_versionProvider.CurrentVersion}");
        }
        else
        {
            title.Append(_versionProvider.CurrentVersion);
        }

        Application.Current.Windows[0].Title = title.ToString();
    }
}
