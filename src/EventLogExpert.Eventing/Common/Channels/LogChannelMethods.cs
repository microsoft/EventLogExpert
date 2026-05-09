// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Eventing.Common.Channels;

public static class LogChannelMethods
{
    private const string MicrosoftWindowsPrefix = "Microsoft-Windows-";

    /// <summary>Live event log channels hard-coded in MenuBar's File menu — filter these from dynamic log enumeration to avoid duplicates.</summary>
    public static IReadOnlySet<string> HardCodedLiveChannels { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        LogChannelNames.ApplicationLog,
        LogChannelNames.SystemLog,
        LogChannelNames.SecurityLog,
    };

    public static IReadOnlyList<string> GetMenuPath(string logName)
    {
        if (string.IsNullOrWhiteSpace(logName))
        {
            return [];
        }

        var slashIndex = logName.IndexOf('/');
        var providerPart = slashIndex < 0 ? logName : logName[..slashIndex];
        var channelPart = slashIndex < 0 ? string.Empty : logName[(slashIndex + 1)..];

        var segments = new List<string>(4);

        if (providerPart.StartsWith(MicrosoftWindowsPrefix, StringComparison.OrdinalIgnoreCase) &&
            providerPart.Length > MicrosoftWindowsPrefix.Length)
        {
            segments.Add("Microsoft");
            segments.Add("Windows");
            segments.Add(providerPart[MicrosoftWindowsPrefix.Length..]);
        }
        else if (providerPart.Length > 0)
        {
            segments.Add(providerPart);
        }

        if (channelPart.Length > 0)
        {
            foreach (var subChannel in channelPart.Split('/'))
            {
                if (subChannel.Length > 0)
                {
                    segments.Add(subChannel);
                }
            }
        }

        return segments.AsReadOnly();
    }
}
