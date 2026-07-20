// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.Readers;
using Microsoft.Win32.SafeHandles;

namespace EventLogExpert.Eventing.Tests.Readers;

public sealed class ChannelAccessEvaluatorTests
{
    [Theory]
    [InlineData(99u)]
    [InlineData(uint.MaxValue)]
    public void ConvertChannelType_WhenBoxedUInt32IsUndefined_ReturnsNull(uint raw) =>
        Assert.Null(EventLogChannelConfigPropertyReader.ConvertChannelType(raw));

    [Theory]
    [InlineData(0u, EvtChannelType.Admin)]
    [InlineData(1u, EvtChannelType.Operational)]
    [InlineData(2u, EvtChannelType.Analytic)]
    [InlineData(3u, EvtChannelType.Debug)]
    public void ConvertChannelType_WhenValueIsBoxedUInt32_ReturnsMatchingType(uint raw, EvtChannelType expected) =>
        Assert.Equal(expected, EventLogChannelConfigPropertyReader.ConvertChannelType(raw));

    [Fact]
    public void EvaluateAccess_WhenAccessCheckDeniesRead_ReturnsRequiresElevation()
    {
        var native = new FakeChannelAccessNative { AccessCheckStatus = false };
        var evaluator = new NativeChannelAccessEvaluator(native);

        var access = evaluator.EvaluateAccess("O:BAG:SYD:(A;;0x1;;;BA)", isSecurityChannel: false);

        Assert.Equal(ChannelAccess.RequiresElevation, access);
    }

    [Fact]
    public void EvaluateAccess_WhenAccessCheckFails_ReturnsUnknown()
    {
        var native = new FakeChannelAccessNative { AccessCheckReturns = false };
        var evaluator = new NativeChannelAccessEvaluator(native);

        var access = evaluator.EvaluateAccess("O:BAG:SYD:(A;;0x1;;;WD)", isSecurityChannel: false);

        Assert.Equal(ChannelAccess.Unknown, access);
    }

    [Fact]
    public void EvaluateAccess_WhenAccessCheckGrantsRead_ReturnsAccessible()
    {
        var native = new FakeChannelAccessNative { AccessCheckStatus = true };
        var evaluator = new NativeChannelAccessEvaluator(native);

        var access = evaluator.EvaluateAccess("O:BAG:SYD:(A;;0x1;;;WD)", isSecurityChannel: false);

        Assert.Equal(ChannelAccess.Accessible, access);
        Assert.Equal(0x1u, native.DesiredAccess);
        Assert.Equal(0x1u, native.GenericMapping.GenericRead);
        Assert.Equal(1, native.FreeSecurityDescriptorCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("D:(A;;0x1;;;WD)")]
    public void EvaluateAccess_WhenSddlMissingOwnerOrGroup_ReturnsUnknown(string? sddl)
    {
        var native = new FakeChannelAccessNative();
        var evaluator = new NativeChannelAccessEvaluator(native);

        var access = evaluator.EvaluateAccess(sddl, isSecurityChannel: false);

        Assert.Equal(ChannelAccess.Unknown, access);
        Assert.Equal(0, native.ConvertSecurityDescriptorCalls);
    }

    [Fact]
    public void EvaluateAccess_WhenSecurityDeniedAndPrivilegeDisabled_ReturnsRequiresElevation()
    {
        var native = new FakeChannelAccessNative
        {
            AccessCheckStatus = false,
            PrivilegeCheckResult = false
        };
        var evaluator = new NativeChannelAccessEvaluator(native);

        var access = evaluator.EvaluateAccess("O:BAG:SYD:(A;;0x1;;;BA)", isSecurityChannel: true);

        Assert.Equal(ChannelAccess.RequiresElevation, access);
    }

    [Fact]
    public void EvaluateAccess_WhenSecurityDeniedAndPrivilegeEnabled_ReturnsAccessible()
    {
        var native = new FakeChannelAccessNative
        {
            AccessCheckStatus = false,
            PrivilegeCheckResult = true
        };
        var evaluator = new NativeChannelAccessEvaluator(native);

        var access = evaluator.EvaluateAccess("O:BAG:SYD:(A;;0x1;;;BA)", isSecurityChannel: true);

        Assert.Equal(ChannelAccess.Accessible, access);
    }

    [Fact]
    public void EvaluateAccess_WhenSecurityPrivilegeCheckFails_ReturnsUnknown()
    {
        var native = new FakeChannelAccessNative
        {
            AccessCheckStatus = false,
            PrivilegeCheckReturns = false
        };
        var evaluator = new NativeChannelAccessEvaluator(native);

        var access = evaluator.EvaluateAccess("O:BAG:SYD:(A;;0x1;;;BA)", isSecurityChannel: true);

        Assert.Equal(ChannelAccess.Unknown, access);
    }

    [Theory]
    [InlineData(EvtChannelType.Analytic)]
    [InlineData(EvtChannelType.Debug)]
    public void ReadConfig_WhenChannelTypeIsAnalyticOrDebug_SkipsAccessEvaluation(EvtChannelType type)
    {
        var accessEvaluator = new FakeChannelAccessEvaluator();
        var reader = new EventLogChannelConfigReader(
            new FakeChannelConfigPropertyReader(new ChannelConfigPropertySnapshot(true, "O:BAG:SYD:(A;;0x1;;;WD)", type)),
            accessEvaluator);

        var config = reader.ReadConfig("Microsoft-Windows-Test/Analytic");

        Assert.Equal(ChannelAccess.NotEvaluated, config.Access);
        Assert.Equal(type, config.Type);
        Assert.True(config.Enabled);
        Assert.Equal(0, accessEvaluator.EvaluateAccessCalls);
    }

    [Fact]
    public void ReadConfig_WhenChannelTypeIsOperational_EvaluatesAccessAndPreservesType()
    {
        var accessEvaluator = new FakeChannelAccessEvaluator();
        var reader = new EventLogChannelConfigReader(
            new FakeChannelConfigPropertyReader(
                new ChannelConfigPropertySnapshot(true, "O:BAG:SYD:(A;;0x1;;;WD)", EvtChannelType.Operational)),
            accessEvaluator);

        var config = reader.ReadConfig("Microsoft-Windows-Test/Operational");

        Assert.Equal(EvtChannelType.Operational, config.Type);
        Assert.Equal(ChannelAccess.Accessible, config.Access);
        Assert.Equal(1, accessEvaluator.EvaluateAccessCalls);
    }

    [Fact]
    public void ReadConfig_WhenChannelTypeIsUnreadable_PreservesUnknownTypeAndSkipsAccessEvaluation()
    {
        var accessEvaluator = new FakeChannelAccessEvaluator();
        var reader = new EventLogChannelConfigReader(
            new FakeChannelConfigPropertyReader(new ChannelConfigPropertySnapshot(null, null, null)),
            accessEvaluator);

        var config = reader.ReadConfig("Microsoft-Windows-Test/Operational");

        Assert.Null(config.Type);
        Assert.Equal(ChannelAccess.Unknown, config.Access);
        Assert.Equal(0, accessEvaluator.EvaluateAccessCalls);
    }

    private sealed class FakeChannelAccessEvaluator : IChannelAccessEvaluator
    {
        internal int EvaluateAccessCalls { get; private set; }

        public ChannelAccess EvaluateAccess(string? sddl, bool isSecurityChannel)
        {
            EvaluateAccessCalls++;

            return ChannelAccess.Accessible;
        }
    }

    private sealed class FakeChannelAccessNative : IChannelAccessNative
    {
        private static readonly SafeAccessTokenHandle s_token = SafeAccessTokenHandle.InvalidHandle;

        internal bool AccessCheckReturns { get; init; } = true;

        internal bool AccessCheckStatus { get; init; } = true;

        internal int ConvertSecurityDescriptorCalls { get; private set; }

        internal uint DesiredAccess { get; private set; }

        internal int FreeSecurityDescriptorCalls { get; private set; }

        internal GenericMapping GenericMapping { get; private set; }

        internal bool PrivilegeCheckResult { get; init; }

        internal bool PrivilegeCheckReturns { get; init; } = true;

        public bool AccessCheck(
            IntPtr securityDescriptor,
            SafeAccessTokenHandle token,
            uint desiredAccess,
            ref GenericMapping genericMapping,
            Span<byte> privilegeSet,
            ref uint privilegeSetLength,
            out uint grantedAccess,
            out bool accessStatus)
        {
            DesiredAccess = desiredAccess;
            GenericMapping = genericMapping;
            grantedAccess = AccessCheckStatus ? desiredAccess : 0;
            accessStatus = AccessCheckStatus;

            return AccessCheckReturns;
        }

        public void Dispose() { }

        public void FreeSecurityDescriptor(IntPtr securityDescriptor) => FreeSecurityDescriptorCalls++;

        public void MapGenericMask(ref uint desiredAccess, ref GenericMapping genericMapping) { }

        public bool PrivilegeCheck(SafeAccessTokenHandle token, string privilegeName, out bool result)
        {
            result = PrivilegeCheckResult;

            return PrivilegeCheckReturns;
        }

        public bool TryConvertSecurityDescriptor(string sddl, out IntPtr securityDescriptor)
        {
            ConvertSecurityDescriptorCalls++;
            securityDescriptor = 123;

            return true;
        }

        public bool TryGetImpersonationToken(out SafeAccessTokenHandle token)
        {
            token = s_token;

            return true;
        }
    }

    private sealed class FakeChannelConfigPropertyReader(ChannelConfigPropertySnapshot snapshot) : IChannelConfigPropertyReader
    {
        public ChannelConfigPropertySnapshot ReadProperties(string channelName) => snapshot;
    }
}
