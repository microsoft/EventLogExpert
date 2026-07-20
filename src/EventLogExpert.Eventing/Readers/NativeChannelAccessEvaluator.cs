// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Interop;

namespace EventLogExpert.Eventing.Readers;

internal sealed class NativeChannelAccessEvaluator(IChannelAccessNative native) : IChannelAccessEvaluator
{
    private const uint EvtReadAccess = 0x1;

    private static GenericMapping ChannelGenericMapping => new()
    {
        GenericRead = 0x1,
        GenericWrite = 0x2,
        GenericExecute = 0x4,
        GenericAll = 0x7
    };

    public ChannelAccess EvaluateAccess(string? sddl, bool isSecurityChannel)
    {
        if (string.IsNullOrWhiteSpace(sddl) ||
            !sddl.Contains("O:", StringComparison.Ordinal) ||
            !sddl.Contains("G:", StringComparison.Ordinal) ||
            !native.TryConvertSecurityDescriptor(sddl, out IntPtr securityDescriptor))
        {
            return ChannelAccess.Unknown;
        }

        try
        {
            if (!native.TryGetImpersonationToken(out var token))
            {
                return ChannelAccess.Unknown;
            }

            var desiredAccess = EvtReadAccess;
            var genericMapping = ChannelGenericMapping;
            native.MapGenericMask(ref desiredAccess, ref genericMapping);

            Span<byte> privilegeSet = stackalloc byte[256];
            var privilegeSetLength = (uint)privilegeSet.Length;

            if (!native.AccessCheck(
                    securityDescriptor,
                    token,
                    desiredAccess,
                    ref genericMapping,
                    privilegeSet,
                    ref privilegeSetLength,
                    out _,
                    out bool accessStatus))
            {
                return ChannelAccess.Unknown;
            }

            if (accessStatus)
            {
                return ChannelAccess.Accessible;
            }

            if (!isSecurityChannel)
            {
                return ChannelAccess.RequiresElevation;
            }

            if (!native.PrivilegeCheck(token, "SeSecurityPrivilege", out bool privilegeEnabled))
            {
                return ChannelAccess.Unknown;
            }

            return privilegeEnabled ? ChannelAccess.Accessible : ChannelAccess.RequiresElevation;
        }
        finally
        {
            native.FreeSecurityDescriptor(securityDescriptor);
        }
    }
}
