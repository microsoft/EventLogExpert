// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Common.Versioning;

public interface ICurrentVersionProvider
{
    Version CurrentVersion { get; }

    bool IsAdmin { get; }

    bool IsDevBuild { get; }

    bool IsSupportedOS(Version currentVersion);
}
