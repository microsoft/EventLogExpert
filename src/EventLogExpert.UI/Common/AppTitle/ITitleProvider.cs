// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Common.AppTitle;

public interface ITitleProvider
{
    string GetTitle();

    void SetTitle(string title);
}
