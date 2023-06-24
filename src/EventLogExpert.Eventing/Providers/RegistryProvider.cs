// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace EventLogExpert.Eventing.Providers;

public class RegistryProvider
{
    private readonly Action<string, LogLevel> _tracer;

    public RegistryProvider(string computerName, Action<string, LogLevel> tracer)
    {
        _tracer = tracer;
        ComputerName = computerName;
    }

    public string ComputerName { get; }

    public string GetSystemRoot()
    {
        var hklm = string.IsNullOrEmpty(ComputerName)
            ? Registry.LocalMachine
            : RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, ComputerName);

        var currentVersion = hklm.OpenSubKey("Software\\Microsoft\\Windows NT\\CurrentVersion");
        var systemRoot = (string)currentVersion?.GetValue("SystemRoot");
        return systemRoot;
    }

    /// <summary>
    ///     sounds
    ///     Returns the file paths for the message files for this provider.
    /// </summary>
    /// <param name="providerName"></param>
    /// <returns></returns>
    public IEnumerable<string> GetMessageFilesForLegacyProvider(string providerName)
    {
        _tracer($"GetLegacyProviderFiles called for provider {providerName} on computer {ComputerName}", LogLevel.Debug);

        var hklm = string.IsNullOrEmpty(ComputerName)
            ? Registry.LocalMachine
            : RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, ComputerName);

        var eventLogKey = hklm.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\EventLog");

        if (eventLogKey == null)
        {
            throw new OpenEventLogRegistryKeyFailedException(ComputerName);
        }

        foreach (var logSubKeyName in eventLogKey.GetSubKeyNames())
        {
            // Skip Security and State since it requires elevation
            if (logSubKeyName is "Security" or "State")
            {
                continue;
            }

            var logSubKey = eventLogKey.OpenSubKey(logSubKeyName);
            var providerSubKey = logSubKey?.OpenSubKey(providerName);

            var eventMessageFilePath = providerSubKey?.GetValue("EventMessageFile") as string;

            if (eventMessageFilePath == null)
            {
                continue;
            }

            _tracer($"Found message file for legacy provider {providerName} in subkey {providerSubKey.Name}", LogLevel.Debug);

            var categoryMessageFilePath = providerSubKey.GetValue("CategoryMessageFile") as string;

            // Filter by extension. The FltMgr provider puts a .sys file in the EventMessageFile value,
            // and trying to load that causes an access violation.
            var supportedExtensions = new[] { ".dll", ".exe" };

            var messageFiles = eventMessageFilePath
                .Split(';')
                .Where(path => path != null && supportedExtensions.Contains(Path.GetExtension(path).ToLower()))
                .ToList();

            IEnumerable<string> files;

            if (categoryMessageFilePath != null)
            {
                var fileList = new List<string> { categoryMessageFilePath };
                fileList.AddRange(messageFiles.Where(f => f != categoryMessageFilePath));
                files = fileList;
            }
            else
            {
                files = messageFiles;
            }

            // Now we have all our paths, but they are not expanded yet, so expand them
            files = GetExpandedFilePaths(files).ToList();

            hklm.Close();
            return files;
        }

        hklm.Close();
        return new List<string>();
    }

    private IEnumerable<string> GetExpandedFilePaths(IEnumerable<string> paths)
    {
        if (string.IsNullOrEmpty(ComputerName))
        {
            // For local computer, do it the easy way
            return paths.Select(Environment.ExpandEnvironmentVariables);
        }

        // For remote computer, get SystemRoot from the registry
        // TODO: Support variables other than SystemRoot?
        var systemRoot = GetSystemRoot();

        if (systemRoot == null)
        {
            throw new ExpandFilePathsFailedException(
                $"Could not get SystemRoot from remote registry: {ComputerName}");
        }

        paths = paths.Select(p =>
        {
            // Expand the variable
            var newPath = p.ReplaceCaseInsensitiveFind("%SystemRoot%", systemRoot);

            // Now replace any drive root references with \\computername\drive$
            var match = Regex.Match(newPath, "^[A-Z]:");

            if (match.Success)
            {
                newPath = $"\\\\{ComputerName}\\{match.Value[0]}${newPath.Substring(2)}";
            }

            return newPath;
        });

        return paths;
    }

    private class ExpandFilePathsFailedException : Exception
    {
        public ExpandFilePathsFailedException(string msg) : base(msg) { }
    }

    private class OpenEventLogRegistryKeyFailedException : Exception
    {
        public OpenEventLogRegistryKeyFailedException(string msg) : base(msg) { }
    }
}
