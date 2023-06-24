// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Services;

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

        builder.Services.AddCoreServices();

        builder.Services.AddBuildServices();
        builder.Services.AddProviderServices();
        builder.Services.AddUiServices();

        return builder.Build();
    }
}
