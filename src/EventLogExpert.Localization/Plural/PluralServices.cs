// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Localization.Plural;

/// <summary>
///     Set-once composition accessor that supplies the plural mechanism's cross-cutting services to the static
///     boundaries (<c>PluralText</c> and the localizable-text resolvers). Before <see cref="Configure" /> is called the
///     accessor returns safe defaults (a no-op diagnostics sink and a <see cref="SharedResource" />-backed neutral pattern
///     provider). Composition sets them exactly once at startup; tests use the internal <see cref="Reset" /> seam to
///     substitute without mutable cross-test leakage.
/// </summary>
public static class PluralServices
{
    private static readonly INeutralPatternProvider s_defaultNeutralPatternProvider = new SharedResourceNeutralPatternProvider();
    private static readonly Lock s_gate = new();
    private static bool s_configured;
    private static Snapshot s_snapshot = new(NoOpPluralDiagnostics.Instance, s_defaultNeutralPatternProvider, Generation: 0);

    /// <summary>A single consistent snapshot of both services plus the configuration generation, read atomically.</summary>
    public static Snapshot Current => Volatile.Read(ref s_snapshot);

    public static IPluralDiagnostics Diagnostics => Volatile.Read(ref s_snapshot).Diagnostics;

    public static INeutralPatternProvider NeutralPatternProvider => Volatile.Read(ref s_snapshot).NeutralPatternProvider;

    public static void Configure(IPluralDiagnostics diagnostics, INeutralPatternProvider neutralPatternProvider)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(neutralPatternProvider);

        lock (s_gate)
        {
            if (s_configured)
            {
                throw new InvalidOperationException("PluralServices has already been configured.");
            }

            s_configured = true;
            Volatile.Write(ref s_snapshot, new Snapshot(diagnostics, neutralPatternProvider, s_snapshot.Generation + 1));
        }
    }

    internal static void Reset()
    {
        lock (s_gate)
        {
            s_configured = false;
            Volatile.Write(ref s_snapshot, new Snapshot(NoOpPluralDiagnostics.Instance, s_defaultNeutralPatternProvider, s_snapshot.Generation + 1));
        }
    }

    public sealed record Snapshot(IPluralDiagnostics Diagnostics, INeutralPatternProvider NeutralPatternProvider, int Generation);
}
