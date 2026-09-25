// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Common.Restart;

public interface IApplicationRestartService
{
    Task<bool> TryRestartAsync(string launchArguments = "");
}
