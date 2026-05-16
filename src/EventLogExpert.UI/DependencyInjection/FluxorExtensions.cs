// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor.DependencyInjection;

namespace EventLogExpert.UI.DependencyInjection;

/// <summary>
///     Fluxor configuration helpers that own the EventLogExpert.UI assembly identity. The host project calls
///     <see cref="RegisterStateLibrary" /> on its <see cref="FluxorOptions" /> so Fluxor's assembly scanner discovers
///     every <c>[FeatureState]</c>, reducer, and effect that lives in this library. Routing the scan through this
///     extension means the assembly identity is anchored on a type defined in the assembly itself — renames or relocations
///     of any individual state type cannot silently misroute the scan to a different assembly.
/// </summary>
public static class FluxorExtensions
{
    /// <summary>
    ///     Adds the EventLogExpert.UI assembly to <paramref name="options" />'s scan list. Chainable with other
    ///     <c>FluxorOptions</c> calls.
    /// </summary>
    public static FluxorOptions RegisterStateLibrary(this FluxorOptions options) =>
        options.ScanAssemblies(typeof(FluxorExtensions).Assembly);
}
