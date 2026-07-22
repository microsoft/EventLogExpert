// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Channels;
using EventLogExpert.Eventing.Interop;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Writers;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace EventLogExpert.Eventing.IntegrationTests.Writers;

public sealed class ChannelConfigWriterIntegrationTests
{
    [Fact]
    public void EnableChannel_WhenChannelAlreadyEnabled_ReturnsAlreadyEnabled()
    {
        // The classic Application log is always enabled; the read-first short-circuit returns without any write.
        var writer = new ChannelConfigWriter();

        var result = writer.EnableChannel(LogChannelNames.ApplicationLog);

        Assert.Equal(ChannelEnableOutcome.AlreadyEnabled, result.Outcome);
    }

    [Fact]
    public void EnableChannel_WhenChannelDisabled_EnablesThenRestoresOriginalState()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EVENTLOG_ALLOW_CHANNEL_MUTATION")))
        {
            Assert.Skip(
                "Enabling a channel is an irreversible machine-wide mutation (it can clear an analytic/debug channel's " +
                "records), so it needs an explicit opt-in beyond EVENTLOG_CONTAINER. The container and CI set " +
                "EVENTLOG_ALLOW_CHANNEL_MUTATION automatically; set it manually only in an ephemeral/throwaway environment.");
        }

        if (!IsElevated())
        {
            Assert.Skip("Enabling a channel persists a machine-wide change and requires an elevated process.");
        }

        using var reader = new EventLogChannelConfigReader();
        var channel = TryFindDisabledNonClassicChannel(reader);

        if (channel is null)
        {
            Assert.Skip("No disabled analytic/debug channel was available to exercise the enable path.");
        }

        var writer = new ChannelConfigWriter();

        try
        {
            var result = writer.EnableChannel(channel);

            Assert.Equal(ChannelEnableOutcome.Enabled, result.Outcome);
            Assert.True(reader.ReadConfig(channel).Enabled);
        }
        finally
        {
            RestoreOriginalDisabledStateOrThrow(reader, channel);
        }
    }

    [Fact]
    public void EnableChannel_WhenChannelNotFound_ReturnsNotFound()
    {
        var writer = new ChannelConfigWriter();

        var result = writer.EnableChannel("EventLogExpert-Nonexistent-Channel/DoesNotExist");

        Assert.Equal(ChannelEnableOutcome.NotFound, result.Outcome);
    }

    private static IEnumerable<string> EnumerateChannels()
    {
        var output = RunWevtutil("el");

        if (output is null) { yield break; }

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return line;
        }
    }

    private static bool IsElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();

        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void RestoreOriginalDisabledStateOrThrow(EventLogChannelConfigReader reader, string channel)
    {
        // The production writer is enable-only, so the test owns the disable via wevtutil. Verify the restore landed
        // and fail loudly if it did not, so a mutation test never silently leaves the machine in a changed state.
        var restored = SetChannelDisabled(channel);

        if (!restored || reader.ReadConfig(channel).Enabled is not false)
        {
            throw new InvalidOperationException(
                $"Failed to restore channel '{channel}' to its original disabled state; the machine may be left mutated.");
        }
    }

    private static string? RunWevtutil(string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("wevtutil.exe", arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            var stdout = new StringBuilder();

            // Drain both streams via the async event model so a full stderr buffer cannot deadlock the child, and
            // enforce the timeout by killing the process rather than blocking indefinitely on a synchronous read.
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) { stdout.AppendLine(e.Data); } };
            process.ErrorDataReceived += (_, _) => { };

            if (!process.Start()) { return null; }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(30_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }

                return null;
            }

            // Flush the async output handlers before reading the accumulated output.
            process.WaitForExit();

            return process.ExitCode == 0 ? stdout.ToString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool SetChannelDisabled(string channel) =>
        RunWevtutil($"sl \"{channel}\" /e:false") is not null;

    private static string? TryFindDisabledNonClassicChannel(EventLogChannelConfigReader reader)
    {
        foreach (var channel in EnumerateChannels())
        {
            ChannelConfig config;

            try { config = reader.ReadConfig(channel); }
            catch { continue; }

            if (config.Enabled == false && config.Type is EvtChannelType.Analytic or EvtChannelType.Debug)
            {
                return channel;
            }
        }

        return null;
    }
}
