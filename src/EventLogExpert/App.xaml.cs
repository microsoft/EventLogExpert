// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventResolvers;
using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert;

public partial class App : Application
{
    public App(IDispatcher fluxorDispatcher, IEventResolver resolver)
    {
        InitializeComponent();

        MainPage = new NavigationPage(new MainPage(fluxorDispatcher, resolver));
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);

        window.Title = "EventLogExpert";

        return window;
    }
}
