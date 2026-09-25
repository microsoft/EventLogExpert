// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Common.AppTitle;

public abstract record AppTitleProgress
{
    public sealed record Installing(int Percent) : AppTitleProgress;

    public sealed record RelaunchToApply : AppTitleProgress;
}
