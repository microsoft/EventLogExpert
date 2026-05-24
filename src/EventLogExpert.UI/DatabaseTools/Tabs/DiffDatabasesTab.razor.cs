// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Microsoft.AspNetCore.Components;

namespace EventLogExpert.UI.DatabaseTools.Tabs;

public sealed partial class DiffDatabasesTab : ComponentBase
{
    public bool IsRunning { get; private set; }

    public void CancelIfRunning() { /* Implemented in step 3.12. */ }
}
