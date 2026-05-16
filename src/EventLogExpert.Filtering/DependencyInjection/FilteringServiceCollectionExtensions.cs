// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace Microsoft.Extensions.DependencyInjection;

public static class FilteringServiceCollectionExtensions
{
    /// <summary>Reserved for future Filtering-domain services (IFilterService stays in MauiProgram, D6/D9).</summary>
    public static IServiceCollection AddEventLogFiltering(this IServiceCollection services) => services;
}
