// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Adapters.Clipboard;
using EventLogExpert.Adapters.FilePicker;
using EventLogExpert.Adapters.FileSave;
using EventLogExpert.Adapters.Menu;
using EventLogExpert.Adapters.Settings;
using EventLogExpert.Adapters.Threading;
using EventLogExpert.Adapters.Window;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Platforms.Windows.Activation;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Activation;
using EventLogExpert.Runtime.Common.AppTitle;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Common.Threading;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.DetailsPane;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Alerts;
using EventLogExpert.WindowsPlatform;

namespace EventLogExpert.DependencyInjection;

/// <summary>
///     Themed grouping extensions that keep <see cref="MauiProgram.CreateMauiApp" /> focused on assembly and
///     lifecycle wiring rather than per-adapter registration churn. Each method represents a cohesive host-side concern.
/// </summary>
internal static class MauiProgramExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddMauiActivationDispatcher()
        {
            services.AddSingleton<IActivationDispatcher>(static provider =>
            {
                var dispatcher = new ActivationDispatcher(
                    provider.GetRequiredService<IAlertDialogService>(),
                    provider.GetRequiredService<ITraceLogger>(),
                    provider.GetRequiredService<IMainThreadService>());

                ActivationBootstrap.AttachDispatcher(dispatcher);

                return dispatcher;
            });

            return services;
        }

        public IServiceCollection AddMauiAlertDialogService()
        {
            services.AddSingleton<IAlertDialogService>(static provider =>
            {
                var modalCoordinator = provider.GetRequiredService<IModalCoordinator>();
                var mainThreadService = provider.GetRequiredService<IMainThreadService>();
                var errorBannerService = provider.GetRequiredService<IErrorBannerService>();
                var infoBannerService = provider.GetRequiredService<IInfoBannerService>();

                return new AlertDialogService(
                    modalCoordinator,
                    mainThreadService,
                    errorBannerService,
                    infoBannerService,
                    async parameters =>
                    {
                        ModalOpenResult<bool> result = await modalCoordinator.PushAsync<AlertModal, bool>(
                            parameters as IDictionary<string, object?> ?? new Dictionary<string, object?>(parameters));

                        return result is { WasOpened: true, Result: true };
                    },
                    async parameters =>
                    {
                        ModalOpenResult<string> result = await modalCoordinator.PushAsync<PromptModal, string>(
                            parameters as IDictionary<string, object?> ?? new Dictionary<string, object?>(parameters));

                        return result.WasOpened ? result.Result ?? string.Empty : string.Empty;
                    });
            });

            return services;
        }

        public IServiceCollection AddMauiMenuServices()
        {
            services.AddSingleton<MauiMenuActionService>();
            services.AddSingleton<IMenuActionService>(static provider =>
                provider.GetRequiredService<MauiMenuActionService>());

            return services;
        }

        public IServiceCollection AddMauiPlatformAdapters()
        {
            services.AddSingleton<IMainThreadService, MauiMainThreadService>();
            services.AddSingleton<ITitleProvider, TitleProvider>();
            services.AddSingleton<IClipboardService, MauiClipboardService>();
            services.AddSingleton<IFileSaveService, MauiFileSaveService>();
            services.AddSingleton<IFilePickerService, MauiFilePickerService>();
            services.AddSingleton<IFolderPickerService, MauiFolderPickerService>();

            return services;
        }

        public IServiceCollection AddMauiPreferenceAdapters()
        {
            services.AddSingleton<ILogTablePreferencesProvider, LogTablePreferencesAdapter>();
            services.AddSingleton<ISettingsPreferencesProvider, SettingsPreferencesAdapter>();
            services.AddSingleton<IDetailsPanePreferencesProvider, DetailsPanePreferencesAdapter>();
            services.AddSingleton<IDatabasePreferencesProvider, DatabasePreferencesAdapter>();

            return services;
        }
    }
}
