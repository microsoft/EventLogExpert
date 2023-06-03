// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using System.Diagnostics.Eventing.Reader;

namespace EventLogExpert.Library.EventResolvers
{
    /// <summary>
    /// Turns a System.Diagnostics.Eventing.Reader.EventRecord into an EventLogExpert.Library.Models.DisplayEventModel.
    /// </summary>
    public interface IEventResolver : IDisposable
    {
        public DisplayEventModel Resolve(EventRecord eventRecord, string OwningLogName);

        public string Status { get; }

        public event EventHandler<string>? StatusChanged;
    }
}
