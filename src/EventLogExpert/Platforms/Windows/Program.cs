// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Platforms.Windows.Activation;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using System.Runtime.InteropServices;
using WinRT;

namespace EventLogExpert.WinUI;

/// <summary>
///     Handwritten WinUI entry point. Replaces MAUI's source-generated <c>Main</c> (suppressed via
///     <c>DISABLE_XAML_GENERATED_MAIN</c> in the csproj's Windows TFM) so the shell activation args (cold launch, file, or
///     command line) can be captured via <see cref="AppInstance.GetCurrent" /> and seeded into
///     <see cref="ActivationBootstrap" /> BEFORE
///     <see cref="Application.Start(Microsoft.UI.Xaml.ApplicationInitializationCallback)" /> builds the app. Each launch
///     runs as its own instance and window; the app is intentionally not single-instanced, so a separate open (context
///     menu, double-click, Open With) always creates a new window while only drag-and-drop adds logs to an existing one.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Activation args are fetched from AppInstance below; the entry-point parameter is unused.
        _ = args;
        XamlCheckProcessRequirements();
        ComWrappersSupport.InitializeComWrappers();

        var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        ActivationBootstrap.SeedColdLaunch(activationArgs);

        // Non-static lambda: avoids the static-anonymous-function-cannot-reference-discard
        // restriction on the `_ = new App()` line below.
        Microsoft.UI.Xaml.Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        return 0;
    }

    // Declared explicitly so the call above resolves regardless of XAML generator behavior under
    // DISABLE_XAML_GENERATED_MAIN.
    [DllImport("Microsoft.ui.xaml.dll")]
    private static extern void XamlCheckProcessRequirements();
}
