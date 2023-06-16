// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using System.Linq.Dynamic.Core;
using System.Linq.Dynamic.Core.CustomTypeProviders;

namespace EventLogExpert.UI;

public class EventLogExpertCustomTypeProvider : DefaultDynamicLinqCustomTypeProvider
{
    public override HashSet<Type> GetCustomTypes() => new[] { typeof(DisplayEventModel) }.ToHashSet();

    public static ParsingConfig ParsingConfig => new ParsingConfig
    {
        CustomTypeProvider = new EventLogExpertCustomTypeProvider()
    };
}
