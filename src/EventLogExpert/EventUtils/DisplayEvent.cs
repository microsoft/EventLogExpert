using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventLogExpert.EventUtils
{
    public record DisplayEvent(
        long? RecordId,
        DateTime? TimeCreated,
        int Id,
        string MachineName,
        string LevelDisplayName,
        string ProviderName,
        string TaskDisplayName,
        string Description
        );
}
