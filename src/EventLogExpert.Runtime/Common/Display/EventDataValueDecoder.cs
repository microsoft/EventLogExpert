// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Common.Display;

/// <summary>
///     Decodes the small set of well-known numeric EventData codes the app surfaces in more than one place - the
///     details-pane field explanation and the timeline group-by labels - to human-readable text, so both stay in sync.
///     Fail-open: an unrecognized field or code returns <see langword="null" /> and each caller supplies its own fallback
///     (the details pane shows nothing; the histogram keeps the raw code as a distinct band).
/// </summary>
public static class EventDataValueDecoder
{
    /// <summary>
    ///     The friendly label for <paramref name="code" /> under <paramref name="fieldName" /> (e.g. LogonType 3 =&gt;
    ///     "Network", TicketEncryptionType 23 =&gt; "RC4"), or <see langword="null" /> when the field is not decoded or the
    ///     code is unrecognized.
    /// </summary>
    public static string? TryDecodeLabel(string fieldName, long code)
    {
        if (string.Equals(fieldName, "LogonType", StringComparison.OrdinalIgnoreCase)) { return DecodeLogonType(code); }

        if (string.Equals(fieldName, "TicketEncryptionType", StringComparison.OrdinalIgnoreCase)) { return DecodeTicketEncryptionType(code); }

        return string.Equals(fieldName, "errorCode", StringComparison.OrdinalIgnoreCase) ? DecodeHResult(code) : null;
    }

    // Curated update/servicing failure HRESULTs; an unrecognized code falls back to its hex form at the call site.
    private static string? DecodeHResult(long code) => (uint)code switch
    {
        0x800F081F => "CBS_E_SOURCE_MISSING",
        0x800F0922 => "CBS_E_INSTALLERS_FAILED",
        0x800F0823 => "CBS_E_NEW_SERVICING_STACK_REQUIRED",
        0x80073712 => "ERROR_SXS_COMPONENT_STORE_CORRUPT",
        0x80D05001 => "DO_E_HTTP_BLOCKSIZE_MISMATCH",
        0x80246007 => "WU_E_DM_NOTDOWNLOADED",
        _ => null
    };

    private static string? DecodeLogonType(long code) => code switch
    {
        0 => "System",
        2 => "Interactive",
        3 => "Network",
        4 => "Batch",
        5 => "Service",
        7 => "Unlock",
        8 => "NetworkCleartext",
        9 => "NewCredentials",
        10 => "RemoteInteractive",
        11 => "CachedInteractive",
        12 => "CachedRemoteInteractive",
        13 => "CachedUnlock",
        _ => null
    };

    // Kerberos RFC 3961 encryption types as carried by Security 4768/4769/4770 TicketEncryptionType; the triage split is
    // the weak legacy RC4 (0x17) against modern AES (0x11/0x12).
    private static string? DecodeTicketEncryptionType(long code) => code switch
    {
        1 => "DES-CBC-CRC",
        3 => "DES-CBC-MD5",
        17 => "AES128",
        18 => "AES256",
        23 => "RC4",
        24 => "RC4-EXP",
        _ => null
    };
}
