// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Adapters.ClipboardAdapter;
using EventLogExpert.Adapters.Elevation;
using EventLogExpert.Adapters.FilePickerAdapter;
using EventLogExpert.Adapters.FileSave;
using EventLogExpert.Adapters.Identity;
using EventLogExpert.Adapters.Input;
using EventLogExpert.Adapters.Lifecycle;
using EventLogExpert.Adapters.Menu;
using EventLogExpert.Adapters.Settings;
using EventLogExpert.Adapters.Threading;
using EventLogExpert.Adapters.Window;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.AppTitle;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Elevation;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Common.Identity;
using EventLogExpert.Runtime.Common.Restart;
using EventLogExpert.Runtime.Common.Threading;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.DetailsPane;
using EventLogExpert.Runtime.FilterCache;
using EventLogExpert.Runtime.FilterGroup;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.Database;
using Fluxor;
using Fluxor.DependencyInjection;

namespace EventLogExpert;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

#if RELEASE
        if (Environment.GetCommandLineArgs().Contains("/EnableConsole", StringComparer.OrdinalIgnoreCase))
#endif
        {
            builder.Services.AddBlazorWebViewDeveloperTools();
        }

        builder.Services.AddFluxor(options =>
        {
            options.ScanAssemblies(typeof(MauiProgram).Assembly)
                .RegisterStateLibrary()
                .WithLifetime(StoreLifetime.Singleton);
        });

        // Core Services
        var fileLocationOptions = new FileLocationOptions(FileSystem.AppDataDirectory);
        builder.Services.AddSingleton(fileLocationOptions);
        Directory.CreateDirectory(fileLocationOptions.DatabasePath);

        builder.Services.AddSingleton(_ =>
        {
            HttpClient client = new() { BaseAddress = new Uri("https://api.github.com/") };

            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");

            return client;
        });

        // Build Services
        builder.Services.AddSingleton<IApplicationRestartService, WindowsApplicationRestartService>();

        // Provider Services
        builder.Services.AddEventLogProviderDatabase();
        builder.Services.AddSingleton<IEventResolverCache, EventResolverCache>();
        builder.Services.AddSingleton<IEventXmlResolver, EventXmlResolver>();
        builder.Services.AddTransient<IEventResolver, EventResolver>();

        // Preference Providers
        builder.Services.AddSingleton<ILogTablePreferencesProvider, LogTablePreferencesAdapter>();
        builder.Services.AddSingleton<IFilterGroupPreferencesProvider, FilterGroupPreferencesAdapter>();
        builder.Services.AddSingleton<IFilterCachePreferencesProvider, FilterCachePreferencesAdapter>();
        builder.Services.AddSingleton<ISettingsPreferencesProvider, SettingsPreferencesAdapter>();
        builder.Services.AddSingleton<IDetailsPanePreferencesProvider, DetailsPanePreferencesAdapter>();
        builder.Services.AddSingleton<IDatabasePreferencesProvider, DatabasePreferencesAdapter>();

        // UI Services
        builder.Services.AddEventLogFiltering();
        builder.Services.AddEventLogRuntime();

        builder.Services.AddSingleton<IMainThreadService, MauiMainThreadService>();
        builder.Services.AddSingleton<ITitleProvider, TitleProvider>();
        builder.Services.AddSingleton<IClipboardService, ClipboardService>();
        builder.Services.AddSingleton<IFileSaveService, MauiFileSaveService>();
        builder.Services.AddSingleton<IFilePickerService, MauiFilePickerService>();
        builder.Services.AddSingleton<IFolderPickerService, MauiFolderPickerService>();
        builder.Services.AddSingleton<IWindowsIdentityProvider, WindowsIdentityProvider>();
        builder.Services.AddSingleton<IElevationService, MauiElevationService>();
        builder.Services.AddSingleton<MauiMenuActionService>();

        builder.Services.AddSingleton<IMenuActionService>(static provider =>
            provider.GetRequiredService<MauiMenuActionService>());

        builder.Services.AddSingleton<KeyboardShortcutService>();
        builder.Services.AddSingleton<DatabaseRecoveryHost>();

        builder.Services.AddSingleton<IAlertDialogService>(static provider =>
        {
            var modalCoordinator = provider.GetRequiredService<IModalCoordinator>();
            var mainThreadService = provider.GetRequiredService<IMainThreadService>();
            var bannerService = provider.GetRequiredService<IBannerService>();

            return new AlertDialogService(
                modalCoordinator,
                mainThreadService,
                bannerService,
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

        var mauiApp = builder.Build();

        mauiApp.Services.GetRequiredService<IBannerService>();
        mauiApp.Services.GetRequiredService<DatabaseRecoveryHost>();

        return mauiApp;
    }
}
