// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Compilation;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Menu;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.TestUtils;

internal static class LogTablePaneDependenciesExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddLogTablePaneDependencies()
        {
            services.AddSingleton(Substitute.For<IClipboardService>());
            services.AddSingleton(Substitute.For<IFilterPaneCommands>());
            services.AddSingleton(Substitute.For<IFilterService>());
            services.AddSingleton(Substitute.For<ILogTableCommands>());
            services.AddSingleton(Substitute.For<IMenuService>());
            services.AddSingleton(Substitute.For<ITraceLogger>());

            return services;
        }
    }
}
