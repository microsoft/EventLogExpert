// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.AppTitle;

namespace EventLogExpert.Adapters.Window;

public class TitleProvider : ITitleProvider
{
    public string GetTitle()
    {
        var current = Application.Current;
        return (current?.Windows.Count > 0 ? current.Windows[0].Title : null) ?? "";
    }

    public void SetTitle(string title)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var current = Application.Current;
            var window = current?.Windows.Count > 0 ? current.Windows[0] : null;
            window?.Title = title;
        });
    }
}
