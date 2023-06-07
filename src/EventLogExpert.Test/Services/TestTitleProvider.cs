// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Interfaces;

namespace EventLogExpert.Test.Services;

internal class TestTitleProvider : ITitleProvider
{
    private string _title = "";

    public string GetTitle() => _title;

    public void SetTitle(string title) => _title = title;
}
