// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.DatabaseTools.Tabs;

public sealed partial class ShowProvidersTab : ComponentBase
{
    /// <summary>True while an operation is in flight for this tab.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Best-effort cancel of the in-flight operation when the modal closes.</summary>
    public void CancelIfRunning()
    {
        // Implemented in step 3.9 alongside the real Run/Cancel logic.
    }
}
