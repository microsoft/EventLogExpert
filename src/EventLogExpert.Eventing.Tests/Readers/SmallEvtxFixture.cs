// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Diagnostics;

namespace EventLogExpert.Eventing.Tests.Readers;

/// <summary>
///     Creates a small temporary .evtx file by exporting at most 5 events from the local
///     Application log. Used by tests that need to exercise end-of-results behavior without
///     scanning the entire (potentially millions of records) Application log.
/// </summary>
internal sealed class SmallEvtxFixture : IDisposable
{
    public SmallEvtxFixture()
    {
        FilePath = Path.Combine(Path.GetTempPath(), $"elx-tests-{Guid.NewGuid():N}.evtx");

        // Use the absolute path to wevtutil.exe rather than relying on PATH so the fixture is
        // robust on machines where %PATH% has been customized.
        var wevtutilPath = Path.Combine(Environment.SystemDirectory, "wevtutil.exe");

        // /q with an EventRecordID range bounds the export to at most 5 events. wevtutil epl
        // does not support a /count switch, so XPath is the supported way to cap output.
        var psi = new ProcessStartInfo
        {
            FileName = wevtutilPath,
            ArgumentList =
            {
                "epl",
                "Application",
                FilePath,
                "/q:*[System[EventRecordID>=1 and EventRecordID<=5]]"
            },
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start wevtutil.exe.");

        if (!proc.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }

            throw new InvalidOperationException("wevtutil.exe did not complete within 30 seconds.");
        }

        if (proc.ExitCode != 0)
        {
            var err = proc.StandardError.ReadToEnd();

            throw new InvalidOperationException(
                $"wevtutil.exe exited with code {proc.ExitCode}. Stderr: {err}");
        }
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
}
