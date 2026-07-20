// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Readers;
using System.Security.Principal;

namespace EventLogExpert.Eventing.IntegrationTests.Readers;

public sealed class ChannelAccessEvaluatorIntegrationTests
{
    [Fact]
    public void EvaluateAccess_WhenSddlAllowsOnlyAdministratorsForNonAdmin_ReturnsRequiresElevation()
    {
        if (IsCurrentProcessElevated())
        {
            Assert.Skip("This test requires a non-elevated process token.");
        }

        using var native = new Win32ChannelAccessNative();
        var evaluator = new NativeChannelAccessEvaluator(native);

        var access = evaluator.EvaluateAccess("O:BAG:SYD:(A;;0x1;;;BA)", isSecurityChannel: false);

        Assert.Equal(ChannelAccess.RequiresElevation, access);
    }

    [Fact]
    public void EvaluateAccess_WhenSddlAllowsWorldRead_ReturnsAccessible()
    {
        using var native = new Win32ChannelAccessNative();
        var evaluator = new NativeChannelAccessEvaluator(native);

        var access = evaluator.EvaluateAccess("O:BAG:SYD:(A;;0x1;;;WD)", isSecurityChannel: false);

        Assert.Equal(ChannelAccess.Accessible, access);
    }

    [Fact]
    public void EvaluateAccess_WhenSddlDeniesEveryoneRead_ReturnsRequiresElevation()
    {
        using var native = new Win32ChannelAccessNative();
        var evaluator = new NativeChannelAccessEvaluator(native);

        // Real channel SDDLs grant specific hex rights (0x1), not generic (GR), and AccessCheck does not
        // remap generic rights inside ACEs. An explicit world deny overrides the admin allow for any token.
        var access = evaluator.EvaluateAccess("O:BAG:SYD:(D;;0x1;;;WD)(A;;0x1;;;BA)", isSecurityChannel: false);

        Assert.Equal(ChannelAccess.RequiresElevation, access);
    }

    private static bool IsCurrentProcessElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);

        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
