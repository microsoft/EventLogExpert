using EventLogExpert.Library.EventResolvers;
using EventLogExpert.Store.State;
using Fluxor;
using Fluxor.Extensions;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        builder.Services.AddFluxor(options =>
        {
            options.ScanAssemblies(typeof(MauiProgram).Assembly).WithLifetime(StoreLifetime.Singleton);
            options.ScanAssemblies(typeof(EventLogState).Assembly).WithLifetime(StoreLifetime.Singleton);
        });

        builder.Services.AddSingleton<IEventResolver, EventReaderEventResolver>();

        return builder.Build();
    }
}
