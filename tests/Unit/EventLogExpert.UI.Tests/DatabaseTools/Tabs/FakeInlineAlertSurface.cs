// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Alerts;
using EventLogExpert.UI.DatabaseTools;

namespace EventLogExpert.UI.Tests.DatabaseTools.Tabs;

internal sealed class FakeInlineAlertSurface : IInlineAlertSurface
{
    public List<InlineAlertRequest> Requests { get; } = [];

    public InlineAlertResult Result { get; set; } = new(false, null);

    public Task<InlineAlertResult> ShowInlineAlertAsync(
        InlineAlertRequest request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(Result);
    }
}
