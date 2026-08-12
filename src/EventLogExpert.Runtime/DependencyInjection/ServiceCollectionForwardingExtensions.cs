// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionForwardingExtensions
{
    /// <summary>
    ///     Registers <typeparamref name="TService" /> so it resolves to the single shared
    ///     <typeparamref name="TImplementation" /> singleton instead of constructing a second instance. Register the concrete
    ///     <typeparamref name="TImplementation" /> separately, then forward every additional interface to it.
    /// </summary>
    public static IServiceCollection Forward<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<TService>(static sp => sp.GetRequiredService<TImplementation>());

        return services;
    }
}
