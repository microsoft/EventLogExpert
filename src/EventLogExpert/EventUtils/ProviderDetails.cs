using System.Collections.Generic;
using EventLogExpert.EventUtils;

namespace EventLogExpert.EventUtils
{
    public class ProviderDetails
    {
        public string ProviderName { get; set; }

        /// <summary>
        /// Messages from legacy provider
        /// </summary>
        public List<Message> Messages { get; set; }

        /// <summary>
        /// Events and related items from modern provider
        /// </summary>
        public List<Event> Events { get; set; }
        public List<ValueName> Keywords { get; set; }
        public List<ValueName> Opcodes { get; set; }
        public List<ValueName> Tasks { get; set; }

        public class ValueName
        {
            public long Value { get; set; }
            public string Name { get; set; }
        }
    }
}
