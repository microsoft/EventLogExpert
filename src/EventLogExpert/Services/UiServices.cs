// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Services;

namespace EventLogExpert.Services;

public static class UiServices
{
    public static IServiceCollection AddUiServices(this IServiceCollection services)
    {
        services.AddSingleton<IMainThreadService>(new MainThreadService(MainThread.InvokeOnMainThreadAsync));

        services.AddSingleton<ITitleProvider, TitleProvider>();
        services.AddSingleton<IAppTitleService, AppTitleService>();

        services.AddSingleton<IAlertDialogService>(new AlertDialogService(
            (title, message, cancel) => Application.Current!.MainPage!.DisplayAlert(title, message, cancel),
            async (title, message, accept, cancel) =>
                await Application.Current!.MainPage!.DisplayAlert(title, message, accept, cancel)));

        services.AddSingleton<IPreferencesProvider, PreferencesProvider>();

        return services;
    }
}
