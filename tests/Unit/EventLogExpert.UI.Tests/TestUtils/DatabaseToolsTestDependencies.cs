// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Common.Versioning;
using EventLogExpert.Runtime.DatabaseTools;
using EventLogExpert.Runtime.DatabaseTools.Elevation;
using EventLogExpert.Runtime.DebugLog;
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
            services.AddSingleton(Substitute.For<IElevatedDatabaseToolsRunner>());
            services.AddSingleton(Substitute.For<IFilePickerService>());
            services.AddSingleton(Substitute.For<IFileSaveService>());
            services.AddSingleton(Substitute.For<IClipboardService>());
            services.AddSingleton(Substitute.For<IAlertDialogService>());
            services.AddSingleton(Substitute.For<ICurrentVersionProvider>());
            services.AddSingleton(Substitute.For<IMenuActionService>());
            services.AddSingleton(Substitute.For<ITraceLogger>());
            services.AddOperationLogProgressFactoryMock();

            return services;
        }

        public IServiceCollection AddOperationLogProgressFactoryMock()
        {
            var operationLogProgressFactory = Substitute.For<IOperationLogProgressFactory>();
            operationLogProgressFactory
                .Create(Arg.Any<IProgress<LogRecord>>(), Arg.Any<string>(), Arg.Any<bool>())
                .Returns(callInfo => callInfo.Arg<IProgress<LogRecord>>());
            services.AddSingleton(operationLogProgressFactory);

            return services;
        }
    }
}
