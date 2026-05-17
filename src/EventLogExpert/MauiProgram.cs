// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Alerts;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Services;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.AppTitle;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Common.Identity;
using EventLogExpert.Runtime.Common.Restart;
using EventLogExpert.Runtime.Common.Threading;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.DependencyInjection;
using EventLogExpert.Runtime.DetailsPane;
using EventLogExpert.Runtime.FilterCache;
using EventLogExpert.Runtime.FilterGroup;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.Runtime.Settings;
using Fluxor;

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
        builder.Services.AddSingleton<IEventResolverCache, EventResolverCache>();
        builder.Services.AddSingleton<IEventXmlResolver, EventXmlResolver>();
        builder.Services.AddTransient<IEventResolver, EventResolver>();

        // Preference Providers
        builder.Services.AddSingleton<PreferencesProvider>();
        builder.Services.AddSingleton<ILogTablePreferencesProvider>(static sp => sp.GetRequiredService<PreferencesProvider>());
        builder.Services.AddSingleton<IFilterGroupPreferencesProvider>(static sp => sp.GetRequiredService<PreferencesProvider>());
        builder.Services.AddSingleton<IFilterCachePreferencesProvider>(static sp => sp.GetRequiredService<PreferencesProvider>());
        builder.Services.AddSingleton<ISettingsPreferencesProvider>(static sp => sp.GetRequiredService<PreferencesProvider>());
        builder.Services.AddSingleton<IDetailsPanePreferencesProvider>(static sp => sp.GetRequiredService<PreferencesProvider>());
        builder.Services.AddSingleton<IDatabasePreferencesProvider>(static sp => sp.GetRequiredService<PreferencesProvider>());

        // UI Services
        builder.Services.AddEventLogFiltering();
        builder.Services.RegisterUiLibrary();

        builder.Services.AddSingleton<IMainThreadService, MauiMainThreadService>();
        builder.Services.AddSingleton<ITitleProvider, TitleProvider>();
        builder.Services.AddSingleton<IClipboardService, ClipboardService>();
        builder.Services.AddSingleton<IFileSaveService, MauiFileSaveService>();
        builder.Services.AddSingleton<IFilePickerService, MauiFilePickerService>();
        builder.Services.AddSingleton<IWindowsIdentityProvider, WindowsIdentityProvider>();
        builder.Services.AddSingleton<MauiMenuActionService>();
        builder.Services.AddSingleton<IMenuActionService>(static provider =>
            provider.GetRequiredService<MauiMenuActionService>());
        builder.Services.AddSingleton<KeyboardShortcutService>();

        builder.Services.AddSingleton<IAlertDialogService>(static provider =>
        {
            var modalService = provider.GetRequiredService<IModalService>();
            var inlineAlertHostBroker = provider.GetRequiredService<IInlineAlertHostBroker>();
            var mainThreadService = provider.GetRequiredService<IMainThreadService>();
            var bannerService = provider.GetRequiredService<IBannerService>();

            return new AlertDialogService(
                inlineAlertHostBroker,
                mainThreadService,
                bannerService,
                parameters => modalService.Show<AlertModal, bool>(parameters.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value)),
                async parameters =>
                {
                    string? result = await modalService.Show<PromptModal, string>(parameters.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value));
                    return result ?? string.Empty;
                });
        });

        var mauiApp = builder.Build();

        mauiApp.Services.GetRequiredService<IBannerService>();

        return mauiApp;
    }
}
