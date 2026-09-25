// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Common.AppTitle;

public sealed record AppTitleState(
    AppTitleProgress? Progress,
    bool IsDevBuild,
    bool IsPrerelease,
    bool IsAdmin,
    string Version,
    string? LogName);
