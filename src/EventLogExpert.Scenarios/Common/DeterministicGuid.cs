// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;

namespace EventLogExpert.Scenarios.Common;

/// <summary>Deterministic, name-based GUIDs per RFC 4122 v5 (SHA-1).</summary>
internal static class DeterministicGuid
{
    internal static readonly Guid ScenarioNamespace = new("7d9d3f2a-6c1e-4b8a-9b1d-2f0a6c5e4d31");

    internal static Guid Create(Guid namespaceId, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        byte[] namespaceBytes = namespaceId.ToByteArray();
        SwapToBigEndian(namespaceBytes);

        byte[] nameBytes = Encoding.UTF8.GetBytes(name);

        byte[] combined = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, combined, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, combined, namespaceBytes.Length, nameBytes.Length);

#pragma warning disable CA5350 // v5 GUID requires SHA-1; identity hash, not security
        byte[] hash = SHA1.HashData(combined);
#pragma warning restore CA5350

        byte[] result = new byte[16];
        Array.Copy(hash, result, 16);

        result[6] = (byte)((result[6] & 0x0F) | 0x50);
        result[8] = (byte)((result[8] & 0x3F) | 0x80);

        SwapToBigEndian(result);
        return new Guid(result);
    }

    private static void SwapToBigEndian(byte[] guid)
    {
        (guid[0], guid[3]) = (guid[3], guid[0]);
        (guid[1], guid[2]) = (guid[2], guid[1]);
        (guid[4], guid[5]) = (guid[5], guid[4]);
        (guid[6], guid[7]) = (guid[7], guid[6]);
    }
}
