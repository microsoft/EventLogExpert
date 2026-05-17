// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Common.AppTitle;

public interface ITitleProvider
{
    string GetTitle();

    void SetTitle(string title);
}
