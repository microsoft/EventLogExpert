// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;

namespace EventLogExpert.Library.EventDatabase;

public class DbEventModel : EventModel
{
    public static DbEventModel FromEventModel(EventModel em, string providerName)
    {
        return new DbEventModel
        {
            Description = em.Description,
            Id = em.Id,
            Keywords = em.Keywords,
            Level = em.Level,
            LogName = em.LogName,
            Opcode = em.Opcode,
            ProviderName = providerName,
            ShortId = (short)em.Id,
            Task = em.Task,
            Template = em.Template,
            Version = em.Version
        };
    }

    public string ProviderName { get; set; }

    public short ShortId { get; set; }

    public Guid DbId { get; set; }
}
