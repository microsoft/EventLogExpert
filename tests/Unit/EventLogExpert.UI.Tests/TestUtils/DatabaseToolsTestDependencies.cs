// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Elevation;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Common.Versioning;
using EventLogExpert.Runtime.DatabaseTools;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Menu;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.TestUtils;

internal static class DatabaseToolsTestDependencies
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDatabaseToolsModalDependencies()
        {
            services.AddSingleton(Substitute.For<ILogReloadCoordinator>());

            return services;
        }

        public IServiceCollection AddDatabaseToolsTabDependencies()
        {
            services.AddSingleton(Substitute.For<IDatabaseToolsService>());
            services.AddSingleton(Substitute.For<IFilePickerService>());
            services.AddSingleton(Substitute.For<IFileSaveService>());
            services.AddSingleton(Substitute.For<IClipboardService>());
            services.AddSingleton(Substitute.For<IAlertDialogService>());
            services.AddSingleton(Substitute.For<IElevationService>());
            services.AddSingleton(Substitute.For<ICurrentVersionProvider>());
            services.AddSingleton(Substitute.For<IMenuActionService>());

            return services;
        }
    }
}
