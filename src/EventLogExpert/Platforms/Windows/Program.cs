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
///     <c>DISABLE_XAML_GENERATED_MAIN</c> in the csproj's Windows TFM) so activation interception can run BEFORE
///     <see cref="Application.Start(Microsoft.UI.Xaml.ApplicationInitializationCallback)" /> — the only place the
///     <see cref="AppInstance.FindOrRegisterForKey(string)" /> single-instance contract permits.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Activation args are re-fetched from AppInstance below; the entry-point parameter is unused.
        _ = args;
        XamlCheckProcessRequirements();
        ComWrappersSupport.InitializeComWrappers();

        var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        var keyInstance = AppInstance.FindOrRegisterForKey("eventlogexpert-main");

        if (!keyInstance.IsCurrent)
        {
            keyInstance.RedirectActivationToAsync(activationArgs).AsTask().GetAwaiter().GetResult();

            return 0;
        }

        // Subscribe BEFORE SeedColdLaunch so the gap between FindOrRegisterForKey returning and the
        // Activated handler being attached is a single statement wide. AppInstance.Activated does
        // not buffer; redirects landing in the gap would be silently dropped.
        keyInstance.Activated += (_, eventArgs) => ActivationBootstrap.EnqueueRedirected(eventArgs);
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
