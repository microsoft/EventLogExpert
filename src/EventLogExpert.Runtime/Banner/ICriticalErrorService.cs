// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Banner;

public interface ICriticalErrorService
{
    event Action StateChanged;

    Exception? CurrentCritical { get; }

    void ClearCritical();

    IDisposable RegisterRecoveryCallback(Func<Task> recover);

    void ReportCritical(Exception ex);

    Task TryRecoverAsync();
}
