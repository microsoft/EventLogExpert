// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Providers;

namespace EventLogExpert.Library.EventDatabase;

public class DbValueName : ProviderDetails.ValueName
{
    public static DbValueName FromValueName(ProviderDetails.ValueName valueName, string providerName)
    {
        return new DbValueName
        {
            Name = valueName.Name,
            ProviderName = providerName,
            Value = valueName.Value
        };
    }

    public string ProviderName { get; set; }

    public Guid DbId { get; set; }
}
