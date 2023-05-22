// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventLogExpert.Shared;

public interface INotificationService
{
    /// <summary>
    /// Puts a notification in the status bar. There can be one
    /// notification per owner. Calling Notify with the same owner
    /// and a new message causes the old message to be replaced
    /// with the new one. If message is null, the last notification
    /// for that owner is removed.
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="message"></param>
    public void Notify(string owner, string message);
}

public class NotificationService
{

}
