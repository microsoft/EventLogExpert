// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using EventLogExpert.Runtime.DetailsPane;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Common;

internal static class GlossaryLocalizer
{
    internal static string Description(IStringLocalizer<SharedResource> localizer, GlossaryTerm term) => term switch
    {
        GlossaryTerm.TargetUserName4624 => localizer["Explain_TargetUserName4624"],
        GlossaryTerm.SubjectUserName4624 => localizer["Explain_SubjectUserName4624"],
        GlossaryTerm.TargetUserName4625 => localizer["Explain_TargetUserName4625"],
        GlossaryTerm.AuthenticationPackageName => localizer["Explain_AuthenticationPackageName"],
        GlossaryTerm.LogonProcessName => localizer["Explain_LogonProcessName"],
        GlossaryTerm.LogonType => localizer["Explain_LogonType"],
        GlossaryTerm.IpAddress => localizer["Explain_IpAddress"],
        GlossaryTerm.IpPort => localizer["Explain_IpPort"],
        GlossaryTerm.ProcessName => localizer["Explain_ProcessName"],
        GlossaryTerm.CommandLine => localizer["Explain_CommandLine"],
        GlossaryTerm.WorkstationName => localizer["Explain_WorkstationName"],
        _ => throw new ArgumentOutOfRangeException(nameof(term), term, null)
    };
}
