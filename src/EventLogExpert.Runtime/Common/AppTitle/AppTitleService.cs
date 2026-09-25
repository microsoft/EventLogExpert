// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Versioning;

namespace EventLogExpert.Runtime.Common.AppTitle;

internal sealed class AppTitleService(
    ICurrentVersionProvider versionProvider,
    IAppTitleTextComposer composer) : IAppTitleService
{
    private bool _isPrereleaseBuild;
    private string? _logName;
    private AppTitleProgress? _progress;

    public void SetIsPrerelease(bool isPrerelease) => _isPrereleaseBuild = isPrerelease;

    public void SetLogName(string? logName)
    {
        _logName = logName;
        Compose();
    }

    public void SetProgress(AppTitleProgress? progress)
    {
        _progress = progress;
        Compose();
    }

    private void Compose() => composer.Compose(new AppTitleState(
        _progress,
        versionProvider.IsDevBuild,
        _isPrereleaseBuild,
        versionProvider.IsAdmin,
        versionProvider.CurrentVersion.ToString(),
        _logName));
}
