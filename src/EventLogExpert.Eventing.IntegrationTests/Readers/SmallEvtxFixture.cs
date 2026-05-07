// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Readers;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace EventLogExpert.Eventing.IntegrationTests.Readers;

/// <summary>Creates a small temporary .evtx file by exporting a bounded window of events from the live local Application log, anchored on the most recent EventRecordID.</summary>
internal sealed class SmallEvtxFixture : IDisposable
{
    private const int MinimumExpectedEvents = 2;
    private const int RequestedEvents = 5;

    private static readonly Regex s_recordIdPattern = new(
        @"<EventRecordID>(\d+)</EventRecordID>",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly TimeSpan s_wevtutilTimeout = TimeSpan.FromSeconds(30);

    public SmallEvtxFixture()
    {
        FilePath = Path.Combine(Path.GetTempPath(), $"elx-tests-{Guid.NewGuid():N}.evtx");

        // Use the absolute path to wevtutil.exe rather than relying on PATH so the fixture is
        // robust on machines where %PATH% has been customized.
        var wevtutilPath = Path.Combine(Environment.SystemDirectory, "wevtutil.exe");

        // Probe the live Application log for its most recent EventRecordID. Hard-coding
        // the legacy [1, 5] window only works on hosts where the log has not rolled past
        // record 5 -- not the case on long-running dev boxes or persistent CI agents.
        long latestId = ProbeLatestRecordId(wevtutilPath);
        long oldestId = Math.Max(1, latestId - (RequestedEvents - 1));

        ExportRecordWindow(wevtutilPath, oldestId, latestId);
        VerifyMinimumEvents();
    }

    public string FilePath { get; }

    public void Dispose()
    {
        try
        {
            if (File.Exists(FilePath)) { File.Delete(FilePath); }
        }
        catch
        {
            // Best-effort cleanup: a temp file left behind should not fail the test.
        }
    }

    private static long ProbeLatestRecordId(string wevtutilPath)
    {
        // qe = query events; /c:1 limits to one event; /rd:true reads in reverse-chronological
        // order (newest first); /f:RenderedXml ensures EventRecordID is rendered as an XML
        // element we can parse without depending on locale-formatted text output.
        var (stdout, _) = RunWevtutil(
            wevtutilPath,
            ["qe", "Application", "/c:1", "/rd:true", "/f:RenderedXml"],
            "qe Application");

        var match = s_recordIdPattern.Match(stdout);

        if (!match.Success || !long.TryParse(match.Groups[1].Value, out long id))
        {
            throw new InvalidOperationException(
                "Could not determine the most recent EventRecordID from wevtutil.exe output. " +
                "The local Application log may be empty.");
        }

        return id;
    }

    private static (string Stdout, string Stderr) RunWevtutil(
        string wevtutilPath,
        IEnumerable<string> arguments,
        string commandLabel)
    {
        var psi = new ProcessStartInfo
        {
            FileName = wevtutilPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        foreach (var arg in arguments) { psi.ArgumentList.Add(arg); }

        using var proc = Process.Start(psi) ??
            throw new InvalidOperationException(
                $"Failed to start wevtutil.exe ({commandLabel}).");

        Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = proc.StandardError.ReadToEndAsync();

        if (!proc.WaitForExit(s_wevtutilTimeout))
        {
            try { proc.Kill(true); }
            catch
            { /* best effort */
            }

            throw new InvalidOperationException(
                $"wevtutil.exe ({commandLabel}) did not complete within {s_wevtutilTimeout.TotalSeconds:0} seconds.");
        }

        // The process has exited so both pipes are now closed; the read tasks complete
        // imminently and a brief synchronous wait here cannot hang.
        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();

        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"wevtutil.exe ({commandLabel}) exited with code {proc.ExitCode}. Stderr: {stderr}");
        }

        return (stdout, stderr);
    }

    private void ExportRecordWindow(string wevtutilPath, long oldestId, long latestId)
    {
        // wevtutil epl does not support a /count switch, so XPath against EventRecordID is the
        // supported way to bound the export size.
        _ = RunWevtutil(
            wevtutilPath,
            ["epl", "Application", FilePath, $"/q:*[System[EventRecordID>={oldestId} and EventRecordID<={latestId}]]"],
            "epl Application");
    }

    private void VerifyMinimumEvents()
    {
        // Open the freshly-exported file and count records so a host where the Application
        // log was just cleared (or where the probed window happened to span gaps) produces
        // a clear fixture-level error instead of cryptic failures (e.g. Assert.True(success)
        // on ERROR_NO_MORE_ITEMS) inside test bodies. We use the project's own
        // EventLogReader rather than the BCL event-log reader because the entire
        // EventLogExpert.Eventing project owns the EVT P/Invoke layer and bypassing it
        // (even in a fixture) would defeat the point.
        using var reader = new EventLogReader(FilePath, PathType.FilePath);

        int total = 0;

        while (reader.TryGetEvents(out var batch) && batch.Length > 0)
        {
            total += batch.Length;
        }

        if (total < MinimumExpectedEvents)
        {
            throw new InvalidOperationException(
                $"SmallEvtxFixture exported only {total} event(s) to '{FilePath}'; " +
                $"required at least {MinimumExpectedEvents}. The local Application log " +
                "may be empty or recently cleared.");
        }
    }
}
