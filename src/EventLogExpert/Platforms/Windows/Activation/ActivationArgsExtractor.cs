// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Activation;
using EventLogExpert.WindowsPlatform;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace EventLogExpert.Platforms.Windows.Activation;

internal static class ActivationArgsExtractor
{
    internal static ActivationArgs Extract(AppActivationArguments? args)
    {
        if (args is null) { return ActivationArgs.Empty; }

        return args.Kind switch
        {
            ExtendedActivationKind.File => FromFile(args.Data as IFileActivatedEventArgs),
            ExtendedActivationKind.Launch => FromLaunch(args.Data as ILaunchActivatedEventArgs),
            ExtendedActivationKind.CommandLineLaunch => FromCommandLine(args.Data as ICommandLineActivatedEventArgs),
            _ => ActivationArgs.Empty,
        };
    }

    private static ActivationArgs ClassifyTokens(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0) { return ActivationArgs.Empty; }

        var classified = ActivationTokenClassifier.Classify(tokens, File.Exists, Directory.Exists);

        return new ActivationArgs(classified.EvtxFiles, classified.Folders);
    }

    private static ActivationArgs FromCommandLine(ICommandLineActivatedEventArgs? cli)
    {
        var raw = cli?.Operation?.Arguments;

        return string.IsNullOrWhiteSpace(raw) ? ActivationArgs.Empty : ClassifyTokens(CommandLineToArgvWHelper.Parse(raw));
    }

    private static ActivationArgs FromFile(IFileActivatedEventArgs? fileArgs)
    {
        if (fileArgs?.Files is null || fileArgs.Files.Count == 0)
        {
            return ActivationArgs.Empty;
        }

        var files = new List<string>();
        var folders = new List<string>();

        foreach (var item in fileArgs.Files)
        {
            switch (item)
            {
                case StorageFile file when !string.IsNullOrEmpty(file.Path):
                    files.Add(file.Path);

                    break;
                case StorageFolder folder when !string.IsNullOrEmpty(folder.Path):
                    folders.Add(folder.Path);

                    break;
            }
        }

        return new ActivationArgs(files, folders);
    }

    private static ActivationArgs FromLaunch(ILaunchActivatedEventArgs? launch)
    {
        if (launch is null || string.IsNullOrWhiteSpace(launch.Arguments))
        {
            return ActivationArgs.Empty;
        }

        return ClassifyTokens(CommandLineToArgvWHelper.Parse(launch.Arguments));
    }
}
