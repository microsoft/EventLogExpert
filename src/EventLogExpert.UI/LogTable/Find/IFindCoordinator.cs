// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.LogTable.Find;

public interface IFindCoordinator
{
    void RequestOpen();

    IDisposable SetActivePane(Action openHandler);
}
