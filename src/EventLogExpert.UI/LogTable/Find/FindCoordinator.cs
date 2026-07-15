// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.LogTable.Find;

public sealed class FindCoordinator : IFindCoordinator
{
    private Action? _openHandler;

    public void RequestOpen() => _openHandler?.Invoke();

    public IDisposable SetActivePane(Action openHandler)
    {
        _openHandler = openHandler;

        return new Registration(this, openHandler);
    }

    // Clear only if this token still owns the handler, so a newer pane's registration survives an older pane's disposal.
    private sealed class Registration(FindCoordinator coordinator, Action handler) : IDisposable
    {
        public void Dispose()
        {
            if (ReferenceEquals(coordinator._openHandler, handler)) { coordinator._openHandler = null; }
        }
    }
}
