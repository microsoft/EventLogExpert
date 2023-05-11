// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using IDispatcher = Fluxor.IDispatcher;

namespace EventLogExpert;

public partial class App : Application
{
    public App(IDispatcher fluxorDispatcher)
    {
        InitializeComponent();

        MainPage = new NavigationPage(new MainPage(fluxorDispatcher));
    }
}
