// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.UI.Banner;

internal static class BannerActionGuard
{
    public static async Task RunSafelyAsync(
        Func<Task> action,
        ITraceLogger logger,
        string componentName,
        string handlerName)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            logger.Error($"{componentName}.{handlerName}: threw: {ex}");
        }
    }
}
