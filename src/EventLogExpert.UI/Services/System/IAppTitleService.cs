// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Interfaces;

public interface IAppTitleService
{
    void SetIsPrerelease(bool isPrerelease);

    void SetLogName(string? logName);

    void SetProgressString(string? progressString);
}
