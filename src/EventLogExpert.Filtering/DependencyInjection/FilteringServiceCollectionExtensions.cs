// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace Microsoft.Extensions.DependencyInjection;

public static class FilteringServiceCollectionExtensions
{
    /// <summary>
    /// Reserved for future EventLogExpert.Filtering-domain service registrations. Currently a
    /// no-op because IFilterService and other UI-facing filter services live in the UI layer
    /// (registered via RegisterUiLibrary); this extension exists so the host can call it now
    /// and pick up future Filtering-domain registrations without changing the composition root.
    /// </summary>
    public static IServiceCollection AddEventLogFiltering(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services;
    }
}
