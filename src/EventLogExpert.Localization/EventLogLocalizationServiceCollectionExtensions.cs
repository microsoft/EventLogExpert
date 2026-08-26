// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace Microsoft.Extensions.DependencyInjection;

public static class EventLogLocalizationServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Registers <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}" /> against the <c>Resources/</c>
        ///     RESX; omits logging deliberately since TryAdd-ing a fallback <c>ILoggerFactory</c> could clobber the host's real
        ///     one by registration order (the host/bUnit/guard-test supply it).
        /// </summary>
        public IServiceCollection AddEventLogLocalization()
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddLocalization(options => options.ResourcesPath = "Resources");

            return services;
        }
    }
}
