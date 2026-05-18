// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace Fluxor.DependencyInjection;

public static class FluxorExtensions
{
    public static FluxorOptions RegisterStateLibrary(this FluxorOptions options) =>
        options.ScanAssemblies(typeof(FluxorExtensions).Assembly);
}
