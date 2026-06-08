// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.WindowsPlatform.Activation;

public static class ActivationTokenClassifier
{
    /// <summary>
    ///     Splits <paramref name="tokens" /> into <c>.evtx</c> files and folders.
    ///     <list type="bullet">
    ///         <item>Tokens that pass neither <paramref name="fileExists" /> nor <paramref name="dirExists" /> are dropped.</item>
    ///         <item>Existing files whose extension is not <c>.evtx</c> (case-insensitive) are dropped.</item>
    ///         <item>Whitespace and <c>null</c> tokens are dropped.</item>
    ///         <item>Per-token filesystem-probe exceptions are caught and the token is dropped.</item>
    ///     </list>
    /// </summary>
    public static Classified Classify(
        IEnumerable<string> tokens,
        Func<string, bool> fileExists,
        Func<string, bool> dirExists)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(fileExists);
        ArgumentNullException.ThrowIfNull(dirExists);

        var files = new List<string>();
        var folders = new List<string>();

        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token)) { continue; }

            try
            {
                if (fileExists(token))
                {
                    if (Path.GetExtension(token).Equals(".evtx", StringComparison.OrdinalIgnoreCase))
                    {
                        files.Add(token);
                    }
                }
                else if (dirExists(token))
                {
                    folders.Add(token);
                }
            }
            catch (Exception)
            {
                // Per-token isolation - drop bad tokens, keep classifying the rest.
            }
        }

        return new Classified(files, folders);
    }

    public sealed record Classified(IReadOnlyList<string> EvtxFiles, IReadOnlyList<string> Folders);
}
