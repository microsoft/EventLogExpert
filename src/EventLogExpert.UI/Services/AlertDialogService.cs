// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Services;

public interface IAlertDialogService
{
    Task ShowAlert(string title, string message, string cancel);

    Task<bool> ShowAlert(string title, string message, string accept, string cancel);
}

public class AlertDialogService : IAlertDialogService
{
    private readonly Func<string, string, string, Task> _oneButtonAlert;
    private readonly Func<string, string, string, string, Task<bool>> _twoButtonAlert;

    public AlertDialogService(Func<string, string, string, Task> oneButtonAlert, Func<string, string, string, string, Task<bool>> twoButtonAlert)
    {
        _oneButtonAlert = oneButtonAlert;
        _twoButtonAlert = twoButtonAlert;
    }

    public async Task ShowAlert(string title, string message, string cancel)
    {
        await _oneButtonAlert(title, message, cancel);
    }

    public async Task<bool> ShowAlert(string title, string message, string accept, string cancel)
    {
        return await _twoButtonAlert(title, message, accept, cancel);
    }
}
