// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EventLogExpert.DatabaseTools.DependencyInjection;

public static class DatabaseToolsServiceCollectionExtensions
{
    /// <summary>
    ///     Registers <see cref="IDatabaseToolsOperationFactory" /> with its production implementation. Idempotent (uses
    ///     <c>TryAddSingleton</c>) so callers can invoke from multiple composition roots without double-registration concerns.
    /// </summary>
    public static IServiceCollection AddDatabaseToolsServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IDatabaseToolsOperationFactory, DatabaseToolsOperationFactory>();
        return services;
    }
}
