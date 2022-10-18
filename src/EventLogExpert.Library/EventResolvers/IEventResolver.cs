using EventLogExpert.Library.Models;
using System.Diagnostics.Eventing.Reader;

namespace EventLogExpert.Library.EventResolvers
{
    /// <summary>
    /// Turns a System.Diagnostics.Eventing.Reader.EventRecord into an EventLogExpert.Library.Models.DisplayEventModel.
    /// </summary>
    public interface IEventResolver
    {
        public DisplayEventModel Resolve(EventRecord eventRecord);
    }
}
