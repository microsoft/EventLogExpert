﻿// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;

namespace EventLogExpert.Services;

public sealed class TitleProvider : ITitleProvider
{
    public string GetTitle() => Application.Current?.Windows[0].Title ?? "";

    public void SetTitle(string title)
    {
        MainThread.InvokeOnMainThreadAsync(() =>
        {
            var window = Application.Current?.Windows[0];

            if (window is not null)
            {
                window.Title = title;
            }
        });
    }
}
